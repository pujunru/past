namespace Past.Infrastructure.Interop;

/// <summary>
/// Shared marker so the clipboard we write ourselves (paste / copy-to-clipboard)
/// is not re-captured as a new clip. Matched by content within a short window.
/// </summary>
public sealed class SelfCopyGuard
{
    private readonly object _lock = new();
    private string? _lastWritten;
    private DateTime _writtenAtUtc;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    public void MarkWritten(string text)
    {
        lock (_lock)
        {
            _lastWritten = text;
            _writtenAtUtc = DateTime.UtcNow;
        }
    }

    public bool ShouldIgnore(string text)
    {
        lock (_lock)
        {
            if (_lastWritten is null)
                return false;
            if (DateTime.UtcNow - _writtenAtUtc > Window)
                return false;
            return string.Equals(_lastWritten, text, StringComparison.Ordinal);
        }
    }
}
