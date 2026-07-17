using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Past.Core;
using Past.Infrastructure.Interop;
using Past.Infrastructure.Security;
using Past.Infrastructure.Storage;
using Past.Services;
using Windows.UI;

namespace Past.App;

/// <summary>
/// Composition root: builds storage + engine, starts the Win32 message pump,
/// wires capture → history and hotkey → overlay, and owns the tray icon.
/// Everything lives here so the P0 wiring is readable in one place.
/// </summary>
internal sealed class AppHost
{
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private readonly SelfCopyGuard _guard = new();

    private SqliteClipStore? _store;
    private HistoryService? _history;
    private ISettingsStore? _settingsStore;
    private AppSettings _settings = new();
    private MessageWindow? _msg;
    private Win32ClipboardMonitor? _monitor;
    private Win32GlobalHotkey? _hotkey;
    private Win32PasteService? _paste;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private TaskbarIcon? _tray;
    private ToggleMenuFlyoutItem? _pasteOnSelectItem;

    public void Start()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Past");
        Directory.CreateDirectory(dir);

        // Storage + engine (privacy floor: DPAPI-wrapped key, AES-GCM at rest).
        var key = new DpapiKeyProvider(Path.Combine(dir, "key.bin")).GetKey();
        var protector = new AesGcmContentProtector(key);
        _store = new SqliteClipStore($"Data Source={Path.Combine(dir, "past.db")}", protector,
            onRowError: (id, ex) => Diag.Log($"skipped unreadable clip id={id}: {ex.GetType().Name}"));
        _history = new HistoryService(_store, new SystemClock(), new HistoryOptions());

        _settingsStore = new JsonSettingsStore(Path.Combine(dir, "settings.json"));
        _settings = _settingsStore.Load();

        // Win32 message pump for clipboard + hotkey.
        _msg = new MessageWindow();
        _msg.Start();

        _paste = new Win32PasteService(_msg, _guard, Diag.Log);
        _overlay = new OverlayWindow(_history, _paste, _settings);

        _monitor = new Win32ClipboardMonitor(_msg, _guard, Diag.Log);
        _monitor.ClipCaptured += OnClipCaptured;
        _monitor.Start();

        _hotkey = new Win32GlobalHotkey(_msg);
        _hotkey.Pressed += OnHotkey;
        RegisterConfiguredHotkey();

        CreateTray();
    }

    /// <summary>
    /// Apply the user's saved hotkey if they chose one and it still registers; otherwise fall
    /// back to the first available default. This keeps the app usable even if the saved chord
    /// was later taken by another app.
    /// </summary>
    private void RegisterConfiguredHotkey()
    {
        if (_settings.HasCustomHotkey &&
            _hotkey!.Rebind(HotkeyChord.From(_settings.HotkeyModifiers, _settings.HotkeyVk)))
        {
            Diag.Log($"hotkey registered (saved): {_hotkey.ActiveChord!.Display}");
            return;
        }

        var ok = _hotkey!.Register(); // first free chord from HotkeyDefaults.Candidates
        Diag.Log(ok
            ? $"hotkey registered (default): {_hotkey.ActiveChord!.Display}"
            : $"hotkey registration FAILED for all candidates; lastWin32Error={_hotkey.LastWin32Error}");
    }

    // Called from the settings window: register the chord live, and persist it only on success.
    private bool TryApplyHotkey(uint modifiers, uint vk)
    {
        var chord = HotkeyChord.From(modifiers, vk);
        if (!_hotkey!.Rebind(chord))
        {
            Diag.Log($"hotkey rebind rejected: {chord.Display} (win32Error={_hotkey.LastWin32Error})");
            return false;
        }

        _settings.HotkeyModifiers = modifiers;
        _settings.HotkeyVk = vk;
        _settingsStore!.Save(_settings);
        if (_tray is not null)
            _tray.ToolTipText = $"Past — clipboard ({chord.Display})";
        Diag.Log($"hotkey rebound to {chord.Display}");
        return true;
    }

    private void OpenSettings()
    {
        // Reuse a single window; re-activate if already open.
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var current = _hotkey?.ActiveChord ?? HotkeyDefaults.Candidates[0];
        _settingsWindow = new SettingsWindow(
            current, TryApplyHotkey, _settings.PasteOnSelect, SetPasteOnSelect);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    // Single path for the "paste immediately on select" behaviour, shared by the tray quick
    // toggle and the settings window, so the two never drift out of sync.
    private void SetPasteOnSelect(bool value)
    {
        _settings.PasteOnSelect = value;
        _settingsStore!.Save(_settings);
        if (_pasteOnSelectItem is { } item && item.IsChecked != value)
            item.IsChecked = value;
    }

    // Fires on the pump thread; persist off the UI thread.
    private void OnClipCaptured(object? sender, ClipDraft draft) => _ = SafeCaptureAsync(draft);

    private async Task SafeCaptureAsync(ClipDraft draft)
    {
        try
        {
            await _history!.CaptureAsync(draft);
        }
        catch (Exception ex)
        {
            // Never take the app down over one clip, but do not hide the reason either:
            // metadata only, never the clip contents.
            Diag.Log($"capture FAILED for {draft.ContentType}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnHotkey(object? sender, EventArgs e)
    {
        // Capture the app we'll paste back into now, before the overlay steals focus.
        var foreground = ForegroundApp.GetWindow();
        _ui.TryEnqueue(() => _overlay!.ShowNear(foreground));
    }

    /// <summary>
    /// Tear everything down and end the process.
    /// <para>
    /// Quit used to just call Application.Exit(), which does not reliably terminate a
    /// window-less WinUI app: the tray icon and XAML host keep it alive, so the process
    /// lingered with its global hotkey still registered while no longer capturing —
    /// looking alive but doing nothing, and blocking the hotkey for any new instance.
    /// </para>
    /// Release the OS-level things we hold first (hotkey, clipboard listener, database),
    /// then guarantee the exit.
    /// </summary>
    private void Shutdown()
    {
        Diag.Log("shutdown requested");
        try
        {
            _tray?.Dispose();    // remove the icon now, not whenever the user next hovers it
            _hotkey?.Dispose();  // unregister the global hotkey
            _monitor?.Dispose(); // stop listening for clipboard updates
            _msg?.Dispose();     // stop the message pump thread
            _store?.Dispose();   // close the database cleanly
        }
        catch (Exception ex)
        {
            Diag.Log($"shutdown cleanup failed: {ex.GetType().Name}: {ex.Message}");
        }

        Application.Current.Exit();

        // A clipboard manager you cannot quit is worse than one that exits abruptly, and
        // everything we own is already released above.
        Environment.Exit(0);
    }

    private void CreateTray()
    {
        var menu = new MenuFlyout();

        var pause = new ToggleMenuFlyoutItem { Text = "Pause capture" };
        pause.Click += (_, _) => _monitor!.CaptureEnabled = !pause.IsChecked;

        // On by default: pick a clip and it lands in your app immediately. Turn off to get
        // the PasteClip behaviour (clip only goes to the clipboard; you paste it yourself).
        var pasteOnSelect = new ToggleMenuFlyoutItem
        {
            Text = "Paste immediately on select",
            IsChecked = _settings.PasteOnSelect,
        };
        // ToggleMenuFlyoutItem flips IsChecked before Click fires, so this is the new value.
        pasteOnSelect.Click += (_, _) => SetPasteOnSelect(pasteOnSelect.IsChecked);
        _pasteOnSelectItem = pasteOnSelect;

        var clear = new MenuFlyoutItem { Text = "Clear history" };
        clear.Click += async (_, _) => await _history!.ClearAllAsync();

        var settings = new MenuFlyoutItem { Text = "Settings…" };
        settings.Click += (_, _) => OpenSettings();

        var quit = new MenuFlyoutItem { Text = "Quit" };
        quit.Click += (_, _) => Shutdown();

        menu.Items.Add(settings);
        menu.Items.Add(pasteOnSelect);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(pause);
        menu.Items.Add(clear);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(quit);

        var chord = _hotkey?.ActiveChord?.Display ?? "no hotkey available";
        _tray = new TaskbarIcon
        {
            ToolTipText = $"Past — clipboard ({chord})",
            ContextFlyout = menu,
            // The default PopupMenu mode renders the flyout as a native Win32 menu and never
            // raises MenuFlyoutItem.Click, so every tray command silently did nothing.
            // SecondWindow hosts the real XAML flyout, where Click routes normally.
            ContextMenuMode = ContextMenuMode.SecondWindow,
        };

        // Use the real app icon so the tray matches the exe/shortcut rather than a
        // separately drawn glyph. Falls back to the built-in rendering if it is missing.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "past.ico");
        if (File.Exists(iconPath))
            _tray.Icon = new System.Drawing.Icon(iconPath);
        else
            _tray.IconSource = new GeneratedIconSource { Text = "P" };

        try
        {
            _tray.ForceCreate();
            Diag.Log("tray icon created");
        }
        catch (Exception ex)
        {
            Diag.Log($"tray icon FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
