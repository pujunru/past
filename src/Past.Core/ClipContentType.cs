namespace Past.Core;

/// <summary>
/// Kind of clipboard content. P0 handles <see cref="Text"/> only;
/// Image/Files are reserved for a later release so the schema need not change.
/// </summary>
public enum ClipContentType
{
    Text = 0,
    Image = 1,
    Files = 2,
}
