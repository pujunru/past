using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Past.Core;
using Past.Infrastructure.Security;
using Past.Infrastructure.Storage;

namespace Past.Tests;

public class CryptoTests
{
    [Fact]
    public void AesGcm_round_trips_content()
    {
        var protector = new AesGcmContentProtector(RandomNumberGenerator.GetBytes(32));
        const string secret = "clipboard secret 🕵️";

        var payload = protector.Protect(secret);

        Assert.DoesNotContain("clipboard", payload); // not stored in cleartext
        Assert.Equal(secret, protector.Unprotect(payload));
    }
}

public class SqliteClipStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"past_test_{Guid.NewGuid():N}.db");
    private readonly AesGcmContentProtector _protector = new(RandomNumberGenerator.GetBytes(32));
    private readonly SqliteClipStore _store;

    public SqliteClipStoreTests()
    {
        _store = new SqliteClipStore($"Data Source={_dbPath}", _protector);
    }

    private static Clip NewClip(string content, DateTime when) => new()
    {
        Content = content,
        Preview = content,
        Hash = ClipHasher.Hash(content, "salt"),
        SizeBytes = content.Length,
        CreatedUtc = when,
        LastUsedUtc = when,
    };

    [Fact]
    public async Task Insert_and_query_round_trips_with_decryption()
    {
        var t = DateTime.UtcNow;
        await _store.InsertAsync(NewClip("alpha", t));

        var recent = await _store.QueryRecentAsync(null, 10);

        var clip = Assert.Single(recent);
        Assert.Equal("alpha", clip.Content); // decrypted back correctly
        Assert.True(clip.Id > 0);
    }

    [Fact]
    public async Task FindByHash_matches_existing()
    {
        var t = DateTime.UtcNow;
        var clip = NewClip("beta", t);
        await _store.InsertAsync(clip);

        var found = await _store.FindByHashAsync(clip.Hash);

        Assert.NotNull(found);
        Assert.Equal("beta", found!.Content);
    }

    [Fact]
    public async Task Search_matches_encrypted_content_in_memory()
    {
        var t = DateTime.UtcNow;
        await _store.InsertAsync(NewClip("needle in haystack", t));
        await _store.InsertAsync(NewClip("unrelated", t));

        var hits = await _store.QueryRecentAsync("needle", 10);

        Assert.Single(hits);
    }

    [Fact]
    public async Task TrimTo_keeps_only_newest()
    {
        var baseTime = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
            await _store.InsertAsync(NewClip($"c{i}", baseTime.AddSeconds(i)));

        await _store.TrimToAsync(2);

        Assert.Equal(2, await _store.CountAsync());
        var recent = await _store.QueryRecentAsync(null, 10);
        Assert.Equal(new[] { "c4", "c3" }, recent.Select(c => c.Content).ToArray());
    }

    [Fact]
    public async Task Image_blobs_round_trip_and_are_encrypted_at_rest()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var thumb = new byte[] { 9, 8, 7 };
        var clip = new Clip
        {
            ContentType = ClipContentType.Image,
            Content = string.Empty,
            Data = png,
            Thumbnail = thumb,
            PixelWidth = 1920,
            PixelHeight = 1080,
            Preview = "Image 1920×1080",
            Hash = ClipHasher.Hash(png, "salt"),
            SizeBytes = png.Length,
            CreatedUtc = DateTime.UtcNow,
            LastUsedUtc = DateTime.UtcNow,
        };
        await _store.InsertAsync(clip);

        var read = Assert.Single(await _store.QueryRecentAsync(null, 10));
        Assert.Equal(ClipContentType.Image, read.ContentType);
        Assert.Equal(png, read.Data);
        Assert.Equal(thumb, read.Thumbnail);
        Assert.Equal(1920, read.PixelWidth);
        Assert.Equal(1080, read.PixelHeight);

        // The raw blob on disk must not be the plaintext PNG.
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Data FROM Clip LIMIT 1;";
        var stored = (byte[])cmd.ExecuteScalar()!;
        Assert.NotEqual(png, stored);
    }

    [Fact]
    public async Task Image_clips_are_findable_by_searching_their_preview()
    {
        // Image clips have empty Content, so search has to consider the preview.
        await _store.InsertAsync(new Clip
        {
            ContentType = ClipContentType.Image,
            Content = string.Empty,
            Data = [1, 2, 3],
            Preview = "Image 800×600",
            Hash = "h1",
            CreatedUtc = DateTime.UtcNow,
            LastUsedUtc = DateTime.UtcNow,
        });

        Assert.Single(await _store.QueryRecentAsync("image", 10));
    }

    [Fact]
    public async Task Unreadable_rows_are_skipped_not_fatal()
    {
        // A row encrypted under a different key stands in for corruption / a stale DPAPI key.
        var otherKey = new AesGcmContentProtector(RandomNumberGenerator.GetBytes(32));
        var t = DateTime.UtcNow;

        await _store.InsertAsync(NewClip("good-before", t.AddSeconds(1)));

        // Insert a row the real store cannot decrypt, using a separate store on the same file.
        using (var alien = new SqliteClipStore($"Data Source={_dbPath}", otherKey))
            await alien.InsertAsync(NewClip("ENCRYPTED-WITH-WRONG-KEY", t.AddSeconds(2)));

        await _store.InsertAsync(NewClip("good-after", t.AddSeconds(3)));

        long? skipped = null;
        using var resilient = new SqliteClipStore($"Data Source={_dbPath}", _protector,
            onRowError: (id, _) => skipped = id);

        var clips = await resilient.QueryRecentAsync(null, 10);

        // The two readable rows survive; the bad one is skipped, not thrown.
        Assert.Equal(new[] { "good-after", "good-before" }, clips.Select(c => c.Content).ToArray());
        Assert.NotNull(skipped);
    }

    [Fact]
    public async Task Delete_and_clear_remove_rows()
    {
        var t = DateTime.UtcNow;
        var clip = NewClip("gamma", t);
        var id = await _store.InsertAsync(clip);

        await _store.DeleteAsync(id);
        Assert.Equal(0, await _store.CountAsync());

        await _store.InsertAsync(NewClip("delta", t));
        await _store.ClearAllAsync();
        Assert.Equal(0, await _store.CountAsync());
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
