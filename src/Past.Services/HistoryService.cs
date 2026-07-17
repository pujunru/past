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
        var clip = draft.ContentType == ClipContentType.Image
            ? BuildImageClip(draft)
            : BuildTextClip(draft);

        if (clip is null)
            return CaptureOutcome.Skipped;

        var existing = await _store.FindByHashAsync(clip.Hash, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            // Duplicate content: surface it to the top rather than storing twice.
            await _store.TouchAsync(existing.Id, clip.LastUsedUtc, ct).ConfigureAwait(false);
            return CaptureOutcome.Deduped;
        }

        await _store.InsertAsync(clip, ct).ConfigureAwait(false);
        await _store.TrimToAsync(_options.MaxItems, ct).ConfigureAwait(false);
        return CaptureOutcome.Added;
    }

    private Clip? BuildTextClip(ClipDraft draft)
    {
        var content = draft.Text;
        if (string.IsNullOrWhiteSpace(content))
            return null;

        if (content.Length > _options.MaxItemChars)
            return null;

        var now = _clock.UtcNow;
        return new Clip
        {
            ContentType = ClipContentType.Text,
            Content = content,
            Preview = ClipHasher.MakePreview(content),
            Hash = ClipHasher.Hash(content, _options.HashSalt),
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(content),
            SourceApp = draft.SourceApp,
            CreatedUtc = now,
            LastUsedUtc = now,
        };
    }

    private Clip? BuildImageClip(ClipDraft draft)
    {
        var bytes = draft.ImageBytes;
        if (bytes is null || bytes.Length == 0)
            return null;

        // Note: the char cap is a text rule and deliberately does not apply here. Image
        // growth is bounded by MaxItems instead.
        var now = _clock.UtcNow;
        return new Clip
        {
            ContentType = ClipContentType.Image,
            Content = string.Empty,
            Data = bytes,
            Thumbnail = draft.ThumbnailBytes,
            PixelWidth = draft.PixelWidth,
            PixelHeight = draft.PixelHeight,
            Preview = DescribeImage(draft.PixelWidth, draft.PixelHeight),
            Hash = ClipHasher.Hash(bytes, _options.HashSalt),
            SizeBytes = bytes.Length,
            SourceApp = draft.SourceApp,
            CreatedUtc = now,
            LastUsedUtc = now,
        };
    }

    private static string DescribeImage(int width, int height) =>
        width > 0 && height > 0 ? $"Image {width}×{height}" : "Image";

    public Task<IReadOnlyList<Clip>> GetRecentAsync(int limit = 200, CancellationToken ct = default)
        => _store.QueryRecentAsync(null, limit, ct);

    public Task<IReadOnlyList<Clip>> SearchAsync(string? query, int limit = 200, CancellationToken ct = default)
        => _store.QueryRecentAsync(string.IsNullOrWhiteSpace(query) ? null : query.Trim(), limit, ct);

    public Task DeleteAsync(long id, CancellationToken ct = default) => _store.DeleteAsync(id, ct);

    public Task ClearAllAsync(CancellationToken ct = default) => _store.ClearAllAsync(ct);
}
