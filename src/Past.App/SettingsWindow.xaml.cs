using System.Runtime.InteropServices;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Past.Infrastructure.Interop;
using Windows.Graphics;
using Windows.UI;

namespace Past.App;

/// <summary>
/// A small settings window built on the shared design system (see Theme/DesignSystem.xaml),
/// so it reads as part of the same app as the overlay. For now it is just the recall hotkey:
/// pick modifiers and a key, Apply tries to register it, and the result is reported inline —
/// a chord already taken by another app fails cleanly instead of silently doing nothing.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    // Win32 MOD_* flags (kept local so this file needs no interop constants).
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private readonly Func<uint, uint, bool> _tryApply;

    /// <param name="tryApply">
    /// Registers (modifiers, vk) as the live hotkey and persists it, returning success.
    /// </param>
    public SettingsWindow(HotkeyChord current, Func<uint, uint, bool> tryApply)
    {
        _tryApply = tryApply;
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
        AppWindow.Resize(new SizeInt32((int)(400 * scale), (int)(400 * scale)));

        PopulateKeys();
        LoadFrom(current);
        UpdatePreview();
    }

    private void PopulateKeys()
    {
        void Add(string name, uint vk) =>
            KeyCombo.Items.Add(new ComboBoxItem { Content = name, Tag = vk });

        for (uint vk = 0x41; vk <= 0x5A; vk++) Add(((char)vk).ToString(), vk); // A-Z
        for (uint vk = 0x30; vk <= 0x39; vk++) Add(((char)vk).ToString(), vk); // 0-9
        for (uint vk = 0x70; vk <= 0x7B; vk++) Add("F" + (vk - 0x6F), vk);      // F1-F12
        Add("Space", 0x20);
        Add("`", 0xC0);
    }

    private void LoadFrom(HotkeyChord current)
    {
        var mods = current.BareModifiers;
        ChkWin.IsChecked = (mods & ModWin) != 0;
        ChkCtrl.IsChecked = (mods & ModControl) != 0;
        ChkAlt.IsChecked = (mods & ModAlt) != 0;
        ChkShift.IsChecked = (mods & ModShift) != 0;

        foreach (ComboBoxItem item in KeyCombo.Items)
        {
            if ((uint)item.Tag == current.Vk)
            {
                KeyCombo.SelectedItem = item;
                break;
            }
        }
    }

    private uint SelectedModifiers()
    {
        uint mods = 0;
        if (ChkWin.IsChecked == true) mods |= ModWin;
        if (ChkCtrl.IsChecked == true) mods |= ModControl;
        if (ChkAlt.IsChecked == true) mods |= ModAlt;
        if (ChkShift.IsChecked == true) mods |= ModShift;
        return mods;
    }

    private uint? SelectedVk() =>
        KeyCombo.SelectedItem is ComboBoxItem { Tag: uint vk } ? vk : null;

    // Live preview so the chord updates as you toggle, before you commit.
    private void OnSelectionChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        var mods = SelectedModifiers();
        var vk = SelectedVk();
        PreviewText.Text = vk is null
            ? (mods == 0 ? "Pick a shortcut" : ModifierText(mods) + "…")
            : HotkeyChord.From(mods, vk.Value).Display;
    }

    private static string ModifierText(uint mods)
    {
        var parts = new List<string>();
        if ((mods & ModWin) != 0) parts.Add("Win");
        if ((mods & ModControl) != 0) parts.Add("Ctrl");
        if ((mods & ModAlt) != 0) parts.Add("Alt");
        if ((mods & ModShift) != 0) parts.Add("Shift");
        return parts.Count == 0 ? "" : string.Join("+", parts) + "+";
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        var mods = SelectedModifiers();
        if (mods == 0)
        {
            SetStatus("Pick at least one modifier.", error: true);
            return;
        }
        if (SelectedVk() is not uint vk)
        {
            SetStatus("Pick a key.", error: true);
            return;
        }

        if (_tryApply(mods, vk))
            SetStatus($"Saved. Recall is now {HotkeyChord.From(mods, vk).Display}.", error: false);
        else
            SetStatus("That combination is already in use by another app. Try a different one.", error: true);
    }

    private void SetStatus(string message, bool error)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(error
            ? Color.FromArgb(255, 209, 52, 56)   // red
            : Color.FromArgb(255, 16, 137, 62));  // green
        StatusText.FontWeight = FontWeights.SemiBold;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);
}
