namespace Past.Core;

/// <summary>
/// A single stored clipboard entry (P0: the "Recent" collection).
/// </summary>
public sealed class Clip
{
    public long Id { get; set; }

    public ClipContentType ContentType { get; set; } = ClipContentType.Text;

    /// <summary>
    /// Full text content, for <see cref="ClipContentType.Text"/> clips.
    /// Stored encrypted at rest by the repository.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>PNG bytes for <see cref="ClipContentType.Image"/> clips. Encrypted at rest.</summary>
    public byte[]? Data { get; set; }

    /// <summary>
    /// Small PNG preview for image clips, so the strip never has to decode the full image.
    /// Null when the image exceeded <see cref="ClipLimits.MaxThumbnailSourceBytes"/>.
    /// </summary>
    public byte[]? Thumbnail { get; set; }

    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }

    /// <summary>Short single-line text shown in lists (derived from the content).</summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>Salted hash of the plaintext/bytes, used for dedupe. Never the raw content.</summary>
    public string Hash { get; set; } = string.Empty;

    public int SizeBytes { get; set; }

    /// <summary>Process name of the app that owned the foreground when captured, if known.</summary>
    public string? SourceApp { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>Last time this clip was captured again or pasted; drives ordering.</summary>
    public DateTime LastUsedUtc { get; set; }
}
