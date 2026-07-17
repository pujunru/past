using System.Security.Cryptography;

namespace Past.Infrastructure.Interop;

/// <summary>
/// Shared marker so the clipboard we write ourselves (paste / copy-to-clipboard)
/// is not re-captured as a new clip. Matched by content within a short window.
/// </summary>
public sealed class SelfCopyGuard
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);
    private readonly object _lock = new();
    private string? _lastWritten;
    private string? _lastImageHash;
    private DateTime _writtenAtUtc;

    public void MarkWritten(string text)
    {
        lock (_lock)
        {
            _lastWritten = text;
            _lastImageHash = null;
            _writtenAtUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Images are matched by hash rather than by value: comparing multi-megabyte buffers on
    /// every clipboard update would be wasteful.
    /// </summary>
    public void MarkImageWritten(byte[] png)
    {
        lock (_lock)
        {
            _lastImageHash = HashOf(png);
            _lastWritten = null;
            _writtenAtUtc = DateTime.UtcNow;
        }
    }

    public bool ShouldIgnore(string text)
    {
        lock (_lock)
        {
            if (_lastWritten is null || Expired())
                return false;
            return string.Equals(_lastWritten, text, StringComparison.Ordinal);
        }
    }

    public bool ShouldIgnoreImage(byte[] png)
    {
        lock (_lock)
        {
            if (_lastImageHash is null || Expired())
                return false;
            return string.Equals(_lastImageHash, HashOf(png), StringComparison.Ordinal);
        }
    }

    private bool Expired() => DateTime.UtcNow - _writtenAtUtc > Window;

    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
