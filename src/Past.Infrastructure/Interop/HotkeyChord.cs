using static Past.Infrastructure.Interop.NativeMethods;

namespace Past.Infrastructure.Interop;

/// <summary>A global hotkey combination and its human-readable name.</summary>
public sealed record HotkeyChord(uint Modifiers, uint Vk, string Display);

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
