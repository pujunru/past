namespace Past.Services;

/// <summary>
/// User-adjustable preferences. P0 keeps this tiny (no settings window yet) — the tray
/// menu toggles what's here, and the full settings UI arrives in P1.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// When true (default), choosing a clip puts it on the clipboard AND pastes it straight
    /// into the app you came from. When false, it is only placed on the clipboard and you
    /// paste it yourself — the macOS PasteClip behaviour.
    /// </summary>
    public bool PasteOnSelect { get; set; } = true;
}

/// <summary>Persists <see cref="AppSettings"/> across runs.</summary>
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
