using Past.Core;

namespace Past.Services;

/// <summary>Wall clock, abstracted for testability.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Persistence for clips. Implemented by the SQLite repository in Infrastructure.
/// Ordering is always most-recently-used first.
/// </summary>
public interface IClipStore
{
    Task<Clip?> FindByHashAsync(string hash, CancellationToken ct = default);
    Task<long> InsertAsync(Clip clip, CancellationToken ct = default);

    /// <summary>Bump ordering timestamp so an existing clip returns to the top.</summary>
    Task TouchAsync(long id, DateTime lastUsedUtc, CancellationToken ct = default);

    /// <summary>Recent clips, newest first. When <paramref name="search"/> is set,
    /// only clips whose preview/content contains it (case-insensitive) are returned.</summary>
    Task<IReadOnlyList<Clip>> QueryRecentAsync(string? search, int limit, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
    Task ClearAllAsync(CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Drop oldest clips until at most <paramref name="maxCount"/> remain.</summary>
    Task TrimToAsync(int maxCount, CancellationToken ct = default);
}

/// <summary>
/// Platform clipboard listener. Raises <see cref="ClipCaptured"/> for each new clip.
/// Implemented over the Win32 clipboard-format listener in Infrastructure.
/// </summary>
public interface IClipboardMonitor : IDisposable
{
    event EventHandler<ClipDraft>? ClipCaptured;
    void Start();
    void Stop();
}

/// <summary>Registers a global hotkey and raises <see cref="Pressed"/> when it fires.</summary>
public interface IGlobalHotkey : IDisposable
{
    event EventHandler? Pressed;
    bool Register();
    void Unregister();
}

/// <summary>Puts a clip on the system clipboard and pastes it into the prior foreground app.</summary>
public interface IPasteService
{
    void SetClipboardText(string text);

    /// <summary>Put a PNG-encoded image on the clipboard (converted to the native format).</summary>
    void SetClipboardImage(byte[] png);

    void PasteInto(nint targetWindow);
}
