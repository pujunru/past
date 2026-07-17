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

    /// <summary>
    /// Modifier flags for the recall hotkey. Match the Win32 MOD_* values so the interop
    /// layer can use them directly: Alt=1, Ctrl=2, Shift=4, Win=8.
    /// </summary>
    public uint HotkeyModifiers { get; set; }

    /// <summary>Virtual-key code for the recall hotkey (e.g. 0x56 = V).</summary>
    public uint HotkeyVk { get; set; }

    /// <summary>False until the user has chosen a hotkey; the app then picks a default.</summary>
    public bool HasCustomHotkey => HotkeyVk != 0;
}

/// <summary>Persists <see cref="AppSettings"/> across runs.</summary>
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
