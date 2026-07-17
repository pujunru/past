using static Past.Infrastructure.Interop.NativeMethods;

namespace Past.Infrastructure.Interop;

/// <summary>A global hotkey combination and its human-readable name.</summary>
public sealed record HotkeyChord(uint Modifiers, uint Vk, string Display)
{
    /// <summary>
    /// Build a chord from bare modifier flags (Alt=1/Ctrl=2/Shift=4/Win=8, no MOD_NOREPEAT)
    /// and a virtual-key code, deriving a readable "Win+Shift+V" style display string.
    /// </summary>
    public static HotkeyChord From(uint modifiers, uint vk) =>
        new(modifiers | MOD_NOREPEAT, vk, Format(modifiers, vk));

    private static string Format(uint modifiers, uint vk)
    {
        var parts = new List<string>();
        if ((modifiers & MOD_WIN) != 0) parts.Add("Win");
        if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(KeyName(vk));
        return string.Join("+", parts);
    }

    /// <summary>Modifier flags without MOD_NOREPEAT, for round-tripping to settings.</summary>
    public uint BareModifiers => Modifiers & ~MOD_NOREPEAT;

    public static string KeyName(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),          // A-Z
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),          // 0-9
        >= 0x60 and <= 0x69 => "Num" + (vk - 0x60),            // NumPad0-9
        >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),              // F1-F12
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x08 => "Backspace",
        0x2E => "Delete",
        0x2D => "Insert",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => "0x" + vk.ToString("X"),
    };
}

/// <summary>
/// Ordered fallback list for the recall hotkey. Windows reserves Win+V for its built-in
/// clipboard history, and Win+Shift+V is taken on some machines, so we try candidates in
/// order and use the first that registers rather than silently failing.
///
/// Ctrl+Shift+V is deliberately NOT a default: it is free to register globally, but doing
/// so would hijack "paste as plain text" inside every other app.
/// </summary>
public static class HotkeyDefaults
{
    public static IReadOnlyList<HotkeyChord> Candidates { get; } = new[]
    {
        new HotkeyChord(MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_V, "Win+Alt+V"),
        new HotkeyChord(MOD_WIN | MOD_SHIFT | MOD_NOREPEAT, VK_V, "Win+Shift+V"),
        new HotkeyChord(MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_V, "Ctrl+Alt+V"),
        new HotkeyChord(MOD_WIN | MOD_SHIFT | MOD_NOREPEAT, VK_C, "Win+Shift+C"),
    };
}
