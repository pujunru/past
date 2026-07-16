using System.Security.Cryptography;
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
    private readonly SqliteClipStore _store;

    public SqliteClipStoreTests()
    {
        var protector = new AesGcmContentProtector(RandomNumberGenerator.GetBytes(32));
        _store = new SqliteClipStore($"Data Source={_dbPath}", protector);
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
