namespace Past.Core;

/// <summary>Size rules shared by the capture pipeline and the UI.</summary>
public static class ClipLimits
{
    /// <summary>
    /// Images at or below this size get a rendered thumbnail on their card; larger ones are
    /// still captured and still paste, but show a generic image icon instead.
    /// <para>
    /// The cap exists because decoding and scaling a very large bitmap costs real memory and
    /// time on the capture path, which runs for every copy. Showing an icon degrades the
    /// preview rather than the feature.
    /// </para>
    /// </summary>
    public const int MaxThumbnailSourceBytes = 50 * 1024 * 1024; // 50 MB

    /// <summary>Longest edge of a generated thumbnail, in pixels.</summary>
    public const int ThumbnailMaxEdge = 320;
}
