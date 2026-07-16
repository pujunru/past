using Past.Core;

namespace Past.Services;

public enum CaptureOutcome
{
    /// <summary>Stored as a new clip.</summary>
    Added,
    /// <summary>Matched an existing clip; moved back to the top instead of duplicating.</summary>
    Deduped,
    /// <summary>Ignored: empty/whitespace or over the size cap.</summary>
    Skipped,
}

/// <summary>
/// Core clipboard-history logic: dedupe, size/count caps, search, delete.
/// Platform-free and fully unit-testable against any <see cref="IClipStore"/>.
/// </summary>
public sealed class HistoryService
{
    private readonly IClipStore _store;
    private readonly IClock _clock;
    private readonly HistoryOptions _options;

    public HistoryService(IClipStore store, IClock clock, HistoryOptions options)
    {
        _store = store;
        _clock = clock;
        _options = options;
    }

    public async Task<CaptureOutcome> CaptureAsync(ClipDraft draft, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Content))
            return CaptureOutcome.Skipped;

        if (draft.Content.Length > _options.MaxItemChars)
            return CaptureOutcome.Skipped;

        var now = _clock.UtcNow;
        var hash = ClipHasher.Hash(draft.Content, _options.HashSalt);

        var existing = await _store.FindByHashAsync(hash, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            // Duplicate content: surface it to the top rather than storing twice.
            await _store.TouchAsync(existing.Id, now, ct).ConfigureAwait(false);
            return CaptureOutcome.Deduped;
        }

        var clip = new Clip
        {
            ContentType = ClipContentType.Text,
            Content = draft.Content,
            Preview = ClipHasher.MakePreview(draft.Content),
            Hash = hash,
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(draft.Content),
            SourceApp = draft.SourceApp,
            CreatedUtc = now,
            LastUsedUtc = now,
        };

        await _store.InsertAsync(clip, ct).ConfigureAwait(false);
        await _store.TrimToAsync(_options.MaxItems, ct).ConfigureAwait(false);
        return CaptureOutcome.Added;
    }

    public Task<IReadOnlyList<Clip>> GetRecentAsync(int limit = 200, CancellationToken ct = default)
        => _store.QueryRecentAsync(null, limit, ct);

    public Task<IReadOnlyList<Clip>> SearchAsync(string? query, int limit = 200, CancellationToken ct = default)
        => _store.QueryRecentAsync(string.IsNullOrWhiteSpace(query) ? null : query.Trim(), limit, ct);

    public Task DeleteAsync(long id, CancellationToken ct = default) => _store.DeleteAsync(id, ct);

    public Task ClearAllAsync(CancellationToken ct = default) => _store.ClearAllAsync(ct);
}
