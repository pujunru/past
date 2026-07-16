namespace Past.Core;

/// <summary>
/// A single stored clipboard entry (P0: the "Recent" collection).
/// </summary>
public sealed class Clip
{
    public long Id { get; set; }

    public ClipContentType ContentType { get; set; } = ClipContentType.Text;

    /// <summary>Full clip content. Stored encrypted at rest by the repository.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Short single-line text shown in lists (derived from <see cref="Content"/>).</summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>Salted hash of the plaintext, used for dedupe. Never the raw content.</summary>
    public string Hash { get; set; } = string.Empty;

    public int SizeBytes { get; set; }

    /// <summary>Process name of the app that owned the foreground when captured, if known.</summary>
    public string? SourceApp { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>Last time this clip was captured again or pasted; drives ordering.</summary>
    public DateTime LastUsedUtc { get; set; }
}
