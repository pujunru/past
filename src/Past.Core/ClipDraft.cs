namespace Past.Core;

/// <summary>
/// Raw capture handed to the history service before dedupe/caps/persistence.
/// Produced by the platform clipboard monitor.
/// <para>
/// Image bytes arrive already encoded as PNG, with the thumbnail already rendered:
/// decoding and scaling are platform concerns and belong in the monitor, so the history
/// logic stays platform-free.
/// </para>
/// </summary>
public sealed record ClipDraft
{
    private ClipDraft(ClipContentType type, string? text, byte[]? imageBytes,
                      byte[]? thumbnailBytes, int pixelWidth, int pixelHeight, string? sourceApp)
    {
        ContentType = type;
        Text = text;
        ImageBytes = imageBytes;
        ThumbnailBytes = thumbnailBytes;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        SourceApp = sourceApp;
    }

    public ClipContentType ContentType { get; }

    /// <summary>Set for <see cref="ClipContentType.Text"/> clips.</summary>
    public string? Text { get; }

    /// <summary>PNG-encoded image, for <see cref="ClipContentType.Image"/> clips.</summary>
    public byte[]? ImageBytes { get; }

    /// <summary>Small PNG preview. Null when the image was too large to render one.</summary>
    public byte[]? ThumbnailBytes { get; }

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public string? SourceApp { get; }

    public static ClipDraft ForText(string text, string? sourceApp) =>
        new(ClipContentType.Text, text, null, null, 0, 0, sourceApp);

    public static ClipDraft ForImage(byte[] imageBytes, byte[]? thumbnailBytes,
                                     int pixelWidth, int pixelHeight, string? sourceApp) =>
        new(ClipContentType.Image, null, imageBytes, thumbnailBytes, pixelWidth, pixelHeight, sourceApp);
}
