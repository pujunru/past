using System.Runtime.InteropServices;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Past.Infrastructure.Interop;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace Past.App;

/// <summary>
/// A small settings window built on the shared design system (see Theme/DesignSystem.xaml),
/// so it reads as part of the same app as the overlay. For now it is just the recall hotkey.
/// <para>
/// The hotkey is chosen by <em>capture</em>, like the OS keyboard settings: click the control,
/// then press the combination you want. Whatever you press is registered as-is — if it is taken
/// by another app the registration fails and we say so, but we never restrict what you can try.
/// </para>
/// </summary>
public sealed partial class SettingsWindow : Window
{
    // Win32 MOD_* flags (kept local so this file needs no interop constants).
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    // Virtual-key codes we read modifier state from.
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;    // VK_MENU
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private readonly Func<uint, uint, bool> _tryApply;

    private uint _mods;
    private uint _vk;
    private bool _recording;

    /// <param name="tryApply">
    /// Registers (modifiers, vk) as the live hotkey and persists it, returning success.
    /// </param>
    public SettingsWindow(HotkeyChord current, Func<uint, uint, bool> tryApply)
    {
        _tryApply = tryApply;
        _mods = current.BareModifiers;
        _vk = current.Vk;

        InitializeComponent();

        Title = "Past — Settings";
        ExtendsContentIntoTitleBar = true; // our accent header is the title bar

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        // AppWindow.Resize takes physical pixels while XAML lays out in logical units, so on a
        // scaled display a logical-sized window clips its content. Scale by DPI.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1.0;
        AppWindow.Resize(new SizeInt32((int)(380 * scale), (int)(260 * scale)));

        ShowChord();
    }

    // Arm capture: the next key combination pressed on the button becomes the hotkey.
    private void OnStartCapture(object sender, RoutedEventArgs e)
    {
        _recording = true;
        CaptureText.Text = "Press a shortcut…";
        SetStatus("", error: false);
    }

    // Losing focus (click elsewhere, Alt+Tab) abandons an armed capture without changing anything.
    private void OnCaptureLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_recording) return;
        _recording = false;
        ShowChord();
    }

    private void OnCaptureKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording) return;

        // Swallow every key while armed so Space/Enter don't re-fire the button's Click.
        e.Handled = true;

        // Esc cancels without changing the current binding.
        if (e.Key == VirtualKey.Escape)
        {
            _recording = false;
            ShowChord();
            return;
        }

        // A lone modifier isn't a chord yet — keep waiting for the real key.
        if (IsModifier(e.Key)) return;

        var mods = ReadModifiers();
        if (mods == 0)
        {
            // A bare key would be a global hotkey with no modifier — almost always a mistake
            // (it would swallow that key everywhere). Ask for a modifier and keep listening.
            SetStatus("Add a modifier — hold Ctrl, Alt, Shift, or Win with the key.", error: true);
            return;
        }

        _recording = false;
        Apply(mods, (uint)e.Key);
    }

    // Register the captured chord and persist on success; report the outcome either way.
    private void Apply(uint mods, uint vk)
    {
        var chord = HotkeyChord.From(mods, vk);
        if (_tryApply(mods, vk))
        {
            _mods = mods;
            _vk = vk;
            ShowChord();
            SetStatus("Shortcut updated.", error: false);
        }
        else
        {
            // A conflict is fine — we just couldn't take this one. Keep the old binding.
            ShowChord();
            SetStatus($"{chord.Display} is in use by another app. Try a different combination.", error: true);
        }
    }

    private void ShowChord() => CaptureText.Text = HotkeyChord.From(_mods, _vk).Display;

    private static bool IsModifier(VirtualKey key) => key is
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;

    // Read the live modifier state at the moment the key landed. GetKeyState is authoritative for
    // Win too, which XAML KeyDown does not surface as a modifier.
    private static uint ReadModifiers()
    {
        static bool Down(int vk) => (GetKeyState(vk) & 0x8000) != 0;

        uint mods = 0;
        if (Down(VkAlt)) mods |= ModAlt;
        if (Down(VkControl)) mods |= ModControl;
        if (Down(VkShift)) mods |= ModShift;
        if (Down(VkLWin) || Down(VkRWin)) mods |= ModWin;
        return mods;
    }

    private void SetStatus(string message, bool error)
    {
        StatusText.Text = message;
        if (string.IsNullOrEmpty(message)) return;
        StatusText.Opacity = 1.0; // the caption style dims to 0.6; a status must read clearly
        StatusText.Foreground = new SolidColorBrush(error
            ? Color.FromArgb(255, 209, 52, 56)   // red
            : Color.FromArgb(255, 16, 137, 62));  // green
        StatusText.FontWeight = FontWeights.SemiBold;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
