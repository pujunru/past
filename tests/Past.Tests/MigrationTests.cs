using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Past.Core;
using Past.Infrastructure.Security;
using Past.Infrastructure.Storage;

namespace Past.Tests;

/// <summary>
/// The image columns were added after v0.1.0 shipped, so databases in the wild already
/// contain real clipboard history. Opening one must migrate it in place, not lose it.
/// </summary>
public class MigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"past_migrate_{Guid.NewGuid():N}.db");
    private readonly AesGcmContentProtector _protector = new(RandomNumberGenerator.GetBytes(32));

    /// <summary>Creates a database with the original v0.1.0 schema (no image columns).</summary>
    private void CreateLegacyDatabase(string content)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Clip (
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
            """;
        cmd.ExecuteNonQuery();

        using var insert = conn.CreateCommand();
        insert.CommandText = """
            INSERT INTO Clip (ContentType, Content, Preview, Hash, SizeBytes, SourceApp, CreatedUtc, LastUsedUtc)
            VALUES (0, $c, $p, 'legacy-hash', 5, 'notepad', 1000, 2000);
            """;
        insert.Parameters.AddWithValue("$c", _protector.Protect(content));
        insert.Parameters.AddWithValue("$p", _protector.Protect(content));
        insert.ExecuteNonQuery();
    }

    [Fact]
    public async Task Opening_a_v0_1_0_database_migrates_it_and_keeps_existing_clips()
    {
        CreateLegacyDatabase("history from the old version");

        using var store = new SqliteClipStore($"Data Source={_dbPath}", _protector);

        var clip = Assert.Single(await store.QueryRecentAsync(null, 10));
        Assert.Equal("history from the old version", clip.Content);
        Assert.Equal("notepad", clip.SourceApp);
        Assert.Equal(ClipContentType.Text, clip.ContentType);

        // The new columns default sensibly for pre-existing text rows.
        Assert.Null(clip.Data);
        Assert.Null(clip.Thumbnail);
        Assert.Equal(0, clip.PixelWidth);
    }

    [Fact]
    public async Task Migrated_database_accepts_image_clips()
    {
        CreateLegacyDatabase("old text");

        using var store = new SqliteClipStore($"Data Source={_dbPath}", _protector);
        await store.InsertAsync(new Clip
        {
            ContentType = ClipContentType.Image,
            Content = string.Empty,
            Data = [1, 2, 3],
            Thumbnail = [4, 5],
            PixelWidth = 640,
            PixelHeight = 480,
            Preview = "Image 640×480",
            Hash = "img-hash",
            SizeBytes = 3,
            CreatedUtc = DateTime.UtcNow,
            LastUsedUtc = DateTime.UtcNow,
        });

        Assert.Equal(2, await store.CountAsync());
        var newest = (await store.QueryRecentAsync(null, 10))[0];
        Assert.Equal(ClipContentType.Image, newest.ContentType);
        Assert.Equal([1, 2, 3], newest.Data);
    }

    [Fact]
    public async Task Migration_is_idempotent()
    {
        CreateLegacyDatabase("old text");

        // Opening twice must not fail trying to re-add the same columns.
        using (var first = new SqliteClipStore($"Data Source={_dbPath}", _protector)) { }
        using var second = new SqliteClipStore($"Data Source={_dbPath}", _protector);
        Assert.Equal(1, await second.CountAsync());
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
