using System.Runtime.InteropServices;
using Past.Services;
using static Past.Infrastructure.Interop.NativeMethods;

namespace Past.Infrastructure.Interop;

/// <summary>
/// <see cref="IGlobalHotkey"/> via RegisterHotKey. Tries <see cref="HotkeyDefaults.Candidates"/>
/// in order and keeps the first that registers — Win+V is reserved by Windows and
/// Win+Shift+V is already taken on some machines.
/// </summary>
public sealed class Win32GlobalHotkey : IGlobalHotkey
{
    private const int HotkeyId = 1;
    private readonly MessageWindow _window;
    private readonly IReadOnlyList<HotkeyChord> _candidates;
    private bool _registered;

    public event EventHandler? Pressed;

    /// <summary>The chord that actually registered, or null if none did.</summary>
    public HotkeyChord? ActiveChord { get; private set; }

    /// <summary>Win32 error from the last failed attempt (0 when registration succeeded).</summary>
    public int LastWin32Error { get; private set; }

    public Win32GlobalHotkey(MessageWindow window, IReadOnlyList<HotkeyChord>? candidates = null)
    {
        _window = window;
        _candidates = candidates ?? HotkeyDefaults.Candidates;
        _window.MessageReceived += OnMessage;
    }

    public bool Register()
    {
        if (_registered)
            return true;

        // Must run on the window's own thread: RegisterHotKey called from another thread
        // fails with ERROR_WINDOW_OF_OTHER_THREAD (1408).
        _window.Invoke(() =>
        {
            foreach (var chord in _candidates)
            {
                if (RegisterHotKey(_window.Handle, HotkeyId, chord.Modifiers, chord.Vk))
                {
                    ActiveChord = chord;
                    LastWin32Error = 0;
                    _registered = true;
                    return;
                }
                // 1409 = ERROR_HOTKEY_ALREADY_REGISTERED -> try the next candidate.
                LastWin32Error = Marshal.GetLastWin32Error();
            }
        });
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered)
            return;
        _window.Invoke(() => UnregisterHotKey(_window.Handle, HotkeyId));
        _registered = false;
        ActiveChord = null;
    }

    private void OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_HOTKEY && wParam == HotkeyId)
            Pressed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Unregister();
        _window.MessageReceived -= OnMessage;
    }
}
