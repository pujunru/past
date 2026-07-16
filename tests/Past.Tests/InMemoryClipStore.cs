using Past.Core;
using Past.Services;

namespace Past.Tests;

/// <summary>Minimal in-memory <see cref="IClipStore"/> for testing HistoryService logic.</summary>
internal sealed class InMemoryClipStore : IClipStore
{
    private readonly List<Clip> _clips = new();
    private long _nextId = 1;

    public IReadOnlyList<Clip> All => _clips;

    public Task<Clip?> FindByHashAsync(string hash, CancellationToken ct = default)
        => Task.FromResult(_clips.FirstOrDefault(c => c.Hash == hash));

    public Task<long> InsertAsync(Clip clip, CancellationToken ct = default)
    {
        clip.Id = _nextId++;
        _clips.Add(clip);
        return Task.FromResult(clip.Id);
    }

    public Task TouchAsync(long id, DateTime lastUsedUtc, CancellationToken ct = default)
    {
        var clip = _clips.First(c => c.Id == id);
        clip.LastUsedUtc = lastUsedUtc;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Clip>> QueryRecentAsync(string? search, int limit, CancellationToken ct = default)
    {
        IEnumerable<Clip> q = _clips.OrderByDescending(c => c.LastUsedUtc);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(c => c.Content.Contains(search, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<IReadOnlyList<Clip>>(q.Take(limit).ToList());
    }

    public Task DeleteAsync(long id, CancellationToken ct = default)
    {
        _clips.RemoveAll(c => c.Id == id);
        return Task.CompletedTask;
    }

    public Task ClearAllAsync(CancellationToken ct = default)
    {
        _clips.Clear();
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(_clips.Count);

    public Task TrimToAsync(int maxCount, CancellationToken ct = default)
    {
        var keep = _clips.OrderByDescending(c => c.LastUsedUtc).Take(maxCount).ToHashSet();
        _clips.RemoveAll(c => !keep.Contains(c));
        return Task.CompletedTask;
    }
}

/// <summary>Deterministic clock for tests.</summary>
internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan by) => UtcNow += by;
}
