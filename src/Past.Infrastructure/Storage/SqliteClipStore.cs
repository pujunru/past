using Microsoft.Data.Sqlite;
using Past.Core;
using Past.Infrastructure.Security;
using Past.Services;

namespace Past.Infrastructure.Storage;

/// <summary>
/// SQLite-backed clip store. Content and Preview columns are encrypted via
/// <see cref="IContentProtector"/>; the salted dedupe Hash stays in cleartext so it
/// can be indexed. Search decrypts a bounded recent window and filters in memory,
/// which is ample for P0's item cap.
///
/// A single connection is not safe for concurrent commands, so every operation is
/// serialized through <see cref="_gate"/> (captures run on the message-pump thread,
/// searches on the UI thread).
/// </summary>
public sealed class SqliteClipStore : IClipStore, IDisposable
{
    private const int SearchScanCap = 2000;
    private readonly SqliteConnection _conn;
    private readonly IContentProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteClipStore(string connectionString, IContentProtector protector)
    {
        _protector = protector;
        _conn = new SqliteConnection(connectionString);
        _conn.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Clip (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                ContentType INTEGER NOT NULL,
                Content     TEXT    NOT NULL,
                Preview     TEXT    NOT NULL,
                Hash        TEXT    NOT NULL,
                SizeBytes   INTEGER NOT NULL,
                SourceApp   TEXT    NULL,
                CreatedUtc  INTEGER NOT NULL,
                LastUsedUtc INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Clip_LastUsed ON Clip(LastUsedUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_Clip_Hash ON Clip(Hash);
            """;
        cmd.ExecuteNonQuery();
    }

    private async Task<T> WithGate<T>(CancellationToken ct, Func<SqliteCommand, Task<T>> body)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _conn.CreateCommand();
            return await body(cmd).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<Clip?> FindByHashAsync(string hash, CancellationToken ct = default) =>
        WithGate(ct, async cmd =>
        {
            cmd.CommandText = "SELECT * FROM Clip WHERE Hash = $h LIMIT 1;";
            cmd.Parameters.AddWithValue("$h", hash);
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await reader.ReadAsync(ct).ConfigureAwait(false) ? Map(reader) : null;
        });

    public Task<long> InsertAsync(Clip clip, CancellationToken ct = default) =>
        WithGate(ct, async cmd =>
        {
            cmd.CommandText = """
                INSERT INTO Clip (ContentType, Content, Preview, Hash, SizeBytes, SourceApp, CreatedUtc, LastUsedUtc)
                VALUES ($type, $content, $preview, $hash, $size, $app, $created, $used);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$type", (int)clip.ContentType);
            cmd.Parameters.AddWithValue("$content", _protector.Protect(clip.Content));
            cmd.Parameters.AddWithValue("$preview", _protector.Protect(clip.Preview));
            cmd.Parameters.AddWithValue("$hash", clip.Hash);
            cmd.Parameters.AddWithValue("$size", clip.SizeBytes);
            cmd.Parameters.AddWithValue("$app", (object?)clip.SourceApp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", ToUnixMs(clip.CreatedUtc));
            cmd.Parameters.AddWithValue("$used", ToUnixMs(clip.LastUsedUtc));
            var id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            clip.Id = id;
            return id;
        });

    public Task TouchAsync(long id, DateTime lastUsedUtc, CancellationToken ct = default) =>
        WithGate(ct, async cmd =>
        {
            cmd.CommandText = "UPDATE Clip SET LastUsedUtc = $used WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$used", ToUnixMs(lastUsedUtc));
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        });

    public Task<IReadOnlyList<Clip>> QueryRecentAsync(string? search, int limit, CancellationToken ct = default) =>
        WithGate<IReadOnlyList<Clip>>(ct, async cmd =>
        {
            if (string.IsNullOrEmpty(search))
            {
                cmd.CommandText = "SELECT * FROM Clip ORDER BY LastUsedUtc DESC LIMIT $limit;";
                cmd.Parameters.AddWithValue("$limit", limit);
            }
            else
            {
                // Content is encrypted, so filter after decryption over a bounded window.
                cmd.CommandText = "SELECT * FROM Clip ORDER BY LastUsedUtc DESC LIMIT $scan;";
                cmd.Parameters.AddWithValue("$scan", SearchScanCap);
            }

            var results = new List<Clip>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var clip = Map(reader);
                if (string.IsNullOrEmpty(search) ||
                    clip.Content.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(clip);
                    if (results.Count >= limit)
                        break;
                }
            }
            return results;
        });

    public Task DeleteAsync(long id, CancellationToken ct = default) =>
        WithGate(ct, async cmd =>
        {
            cmd.CommandText = "DELETE FROM Clip WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        });

    public Task ClearAllAsync(CancellationToken ct = default) =>
        WithGate(ct, async cmd =>
        {
            cmd.CommandText = "DELETE FROM Clip; VACUUM;";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        });

    public Task<int> CountAsync(CancellationToken ct = default) =>
        WithGate(ct, async cmd =>
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Clip;";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        });

    public Task TrimToAsync(int maxCount, CancellationToken ct = default) =>
        WithGate(ct, async cmd =>
        {
            // Keep the newest maxCount rows; delete everything older.
            cmd.CommandText = """
                DELETE FROM Clip WHERE Id NOT IN (
                    SELECT Id FROM Clip ORDER BY LastUsedUtc DESC LIMIT $max
                );
                """;
            cmd.Parameters.AddWithValue("$max", maxCount);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        });

    private Clip Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")),
        ContentType = (ClipContentType)r.GetInt32(r.GetOrdinal("ContentType")),
        Content = _protector.Unprotect(r.GetString(r.GetOrdinal("Content"))),
        Preview = _protector.Unprotect(r.GetString(r.GetOrdinal("Preview"))),
        Hash = r.GetString(r.GetOrdinal("Hash")),
        SizeBytes = r.GetInt32(r.GetOrdinal("SizeBytes")),
        SourceApp = r.IsDBNull(r.GetOrdinal("SourceApp")) ? null : r.GetString(r.GetOrdinal("SourceApp")),
        CreatedUtc = FromUnixMs(r.GetInt64(r.GetOrdinal("CreatedUtc"))),
        LastUsedUtc = FromUnixMs(r.GetInt64(r.GetOrdinal("LastUsedUtc"))),
    };

    private static long ToUnixMs(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static DateTime FromUnixMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

    public void Dispose()
    {
        _conn.Dispose();
        _gate.Dispose();
    }
}
