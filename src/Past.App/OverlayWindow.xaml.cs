using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Past.Core;
using Past.Infrastructure.Interop;
using Past.Services;
using Windows.Graphics;
using Windows.System;

namespace Past.App;

/// <summary>
/// The recall overlay: a horizontal strip of clip cards that opens at the cursor on the
/// global hotkey, filters as you type, and pastes the chosen clip back into the app you
/// came from. Reused across invocations (hidden, never closed).
/// </summary>
public sealed partial class OverlayWindow : Window
{
    // Logical (DPI-independent) size. Tall enough for a 200px card plus the top bar,
    // margins, row spacing and the horizontal scrollbar.
    private const int LogicalWidth = 940;
    private const int LogicalHeight = 300;
    private const int LogicalBottomMargin = 24; // gap above the taskbar
    private const double DragThreshold = 5.0;   // px moved before a press becomes a drag

    private readonly HistoryService _history;
    private readonly IPasteService _paste;
    private readonly AppSettings _settings;
    private nint _targetWindow;
    private readonly nint _selfHwnd;

    // Physical size, recomputed from the window's DPI.
    private int _pxWidth;
    private int _pxHeight;

    private bool _listFocused;

    // drag state
    private bool _dragCandidate;
    private bool _dragging;
    private POINT _dragStartCursor;
    private PointInt32 _dragStartWindow;

    // Guards click-outside-to-dismiss against the foreground race right after showing:
    // if SetForegroundWindow loses, we'd get a Deactivated and hide ourselves instantly.
    private DateTime _shownAtUtc = DateTime.MinValue;
    private static readonly TimeSpan DeactivateGrace = TimeSpan.FromMilliseconds(600);

    public OverlayWindow(HistoryService history, IPasteService paste, AppSettings settings)
    {
        _history = history;
        _paste = paste;
        _settings = settings;
        InitializeComponent();

        _selfHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        AppWindow.SetPresenter(presenter);
        ApplyDpiSize();

        AppWindow.Closing += (_, e) => { e.Cancel = true; Hide(); }; // never really close
        Activated += OnActivated;

        // handledEventsToo: the ListView marks pointer events handled for selection, so we
        // must still see them to allow dragging the panel by grabbing a card.
        RootBorder.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), true);
        RootBorder.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnRootPointerMoved), true);
        RootBorder.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnRootPointerReleased), true);
    }

    /// <summary>
    /// AppWindow.Resize takes physical pixels while XAML lays out in logical units, so on a
    /// scaled display (e.g. 150%) a logical-sized window clips its content. Scale by DPI.
    /// </summary>
    private void ApplyDpiSize()
    {
        var scale = GetDpiForWindow(_selfHwnd) / 96.0;
        if (scale <= 0) scale = 1.0;
        _pxWidth = (int)Math.Round(LogicalWidth * scale);
        _pxHeight = (int)Math.Round(LogicalHeight * scale);
        AppWindow.Resize(new SizeInt32(_pxWidth, _pxHeight));
    }

    /// <summary>Show the overlay at the cursor, targeting <paramref name="foreground"/> for paste-back.</summary>
    public async void ShowNear(nint foreground)
    {
        try
        {
            _shownAtUtc = DateTime.UtcNow;
            _targetWindow = foreground;
            ApplyDpiSize(); // DPI can differ per monitor / since construction
            MoveToBottomCenter();
            SearchBox.Text = string.Empty;
            await LoadAsync(null);

            AppWindow.Show();
            Activate();

            // Activate() alone does not reliably give a hotkey-shown window keyboard focus:
            // Windows refuses foreground to a background process, so Esc/arrows would go to
            // the app underneath. ForceForeground works around the foreground lock.
            ForegroundApp.ForceForeground(_selfHwnd);

            // Land on the first card, not the search box: the common case is "grab the last
            // thing I copied", which should be one Enter away. Typing still searches (see
            // OnListCharacterReceived).
            FocusFirstCard();

            _shownAtUtc = DateTime.UtcNow; // restart the grace window once actually up
        }
        catch (Exception ex)
        {
            Diag.Log($"overlay show FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Put keyboard focus on the first card.
    /// <para>
    /// ListView.Focus() fails right after ItemsSource is set, because the item containers
    /// have not been realized yet. WinUI then parks focus on whatever else is focusable —
    /// in practice the close button — which handles neither Esc nor arrow keys, so the
    /// overlay looked completely unresponsive. Force a layout pass first, then focus the
    /// real container.
    /// </para>
    /// </summary>
    private void FocusFirstCard()
    {
        if (ResultsList.Items.Count == 0)
        {
            SearchBox.Focus(FocusState.Programmatic);
            return;
        }

        ResultsList.UpdateLayout(); // realize containers so one of them can take focus

        if (ResultsList.ContainerFromIndex(0) is ListViewItem first &&
            first.Focus(FocusState.Programmatic))
            return;

        if (!ResultsList.Focus(FocusState.Programmatic))
            SearchBox.Focus(FocusState.Programmatic);
    }

    private void Hide() => AppWindow.Hide();

    // Escape is handled at the panel root so it closes the overlay no matter where focus
    // sits, rather than relying on every control's KeyDown.
    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Hide();
        args.Handled = true;
    }

    // Click-outside-to-dismiss.
    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated)
            return;
        if (_dragging || DateTime.UtcNow - _shownAtUtc < DeactivateGrace)
            return;
        Hide();
    }

    private async Task LoadAsync(string? query)
    {
        var clips = await _history.SearchAsync(query);
        ResultsList.ItemsSource = clips.Select(c => new ClipCard(c)).ToList();
        if (ResultsList.Items.Count > 0)
            ResultsList.SelectedIndex = 0;
    }

    // The default container chrome is gone, so highlighting is driven from the card.
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshHighlight();

    // Driven by whichever control GAINS focus. Using LostFocus would flicker, because it
    // also fires while moving between cards inside the strip.
    private void OnListGotFocus(object sender, RoutedEventArgs e)
    {
        _listFocused = true;
        RefreshHighlight();
    }

    private void OnSearchGotFocus(object sender, RoutedEventArgs e)
    {
        _listFocused = false;
        RefreshHighlight();
    }

    /// <summary>
    /// Highlight = "selected AND the strip has focus". Without the focus term the first card
    /// looks selected while you're typing in the search box, which reads as a broken state.
    /// </summary>
    private void RefreshHighlight()
    {
        if (ResultsList.ItemsSource is not IEnumerable<ClipCard> cards)
            return;

        var selected = ResultsList.SelectedItem as ClipCard;
        foreach (var card in cards)
            card.IsHighlighted = _listFocused && ReferenceEquals(card, selected);
    }

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        => await LoadAsync(SearchBox.Text);

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                Hide();
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                PasteSelected();
                e.Handled = true;
                break;
            // Cards run left-to-right, so Down or Right both step into the strip.
            case VirtualKey.Down:
            case VirtualKey.Right:
                ResultsList.Focus(FocusState.Programmatic);
                e.Handled = true;
                break;
        }
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            PasteSelected();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Focus starts on the cards, so typing would otherwise go nowhere. Redirect any
    /// printable key into the search box — you can still just open and type.
    /// </summary>
    private void OnListCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        if (char.IsControl(args.Character))
            return; // Enter/Esc/Tab keep their normal behaviour

        SearchBox.Text += args.Character;
        SearchBox.SelectionStart = SearchBox.Text.Length;
        SearchBox.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ClipCard card)
            Paste(card);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void PasteSelected()
    {
        if (ResultsList.SelectedItem is ClipCard card)
            Paste(card);
    }

    private void Paste(ClipCard card)
    {
        Hide();

        if (card.Clip.ContentType == ClipContentType.Image)
        {
            if (card.Clip.Data is null)
                return;
            _paste.SetClipboardImage(card.Clip.Data);
        }
        else
        {
            _paste.SetClipboardText(card.Clip.Content);
        }

        // Default: paste straight into the app you came from. With PasteOnSelect off the
        // clip only lands on the clipboard and the user pastes it themselves.
        if (!_settings.PasteOnSelect)
            return;

        // PasteInto waits for focus to settle and for hotkey modifiers to be released, so
        // keep it off the UI thread.
        var target = _targetWindow;
        Task.Run(() => _paste.PasteInto(target));
    }

    // ---- drag to move ----------------------------------------------------

    // The whole panel is a drag surface, except the controls you need to actually click.
    // A press only becomes a drag once it moves past DragThreshold, so a plain click on a
    // card still selects/pastes.
    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsInteractive(e.OriginalSource as DependencyObject))
            return;
        if (!GetCursorPos(out _dragStartCursor))
            return;

        _dragStartWindow = AppWindow.Position;
        _dragCandidate = true;
    }

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragCandidate || !GetCursorPos(out var now))
            return;

        var dx = now.X - _dragStartCursor.X;
        var dy = now.Y - _dragStartCursor.Y;

        if (!_dragging)
        {
            if (Math.Sqrt((dx * dx) + (dy * dy)) < DragThreshold)
                return;
            // Capture only now, so a simple click is never stolen from the ListView.
            _dragging = true;
            RootBorder.CapturePointer(e.Pointer);
        }

        // Screen coordinates: delta is independent of DPI and client origin.
        AppWindow.Move(new PointInt32(_dragStartWindow.X + dx, _dragStartWindow.Y + dy));
    }

    private void OnRootPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
            RootBorder.ReleasePointerCapture(e.Pointer);
        _dragCandidate = false;
        _dragging = false;
    }

    /// <summary>
    /// True for controls that must keep their own pointer behaviour, so the panel-wide drag
    /// never steals from them. ScrollBar/Thumb matter: dragging the strip's scrollbar was
    /// moving the window instead of scrolling. ButtonBase (not Button) is used so the
    /// scrollbar's RepeatButton arrows are covered too.
    /// </summary>
    private static bool IsInteractive(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBox or ButtonBase or ScrollBar or Thumb)
                return true;
        }
        return false;
    }

    // ---- positioning -----------------------------------------------------

    /// <summary>
    /// Park the strip at the bottom-centre of the display, rather than chasing the cursor.
    /// WorkArea excludes the taskbar, and everything here is physical pixels.
    /// </summary>
    private void MoveToBottomCenter()
    {
        var area = TargetDisplay();
        var work = area.WorkArea;
        var scale = GetDpiForWindow(_selfHwnd) / 96.0;
        if (scale <= 0) scale = 1.0;

        var x = work.X + ((work.Width - _pxWidth) / 2);
        var y = work.Y + work.Height - _pxHeight - (int)Math.Round(LogicalBottomMargin * scale);

        // Never let it spill off a display smaller than the strip.
        x = Math.Max(work.X, x);
        y = Math.Max(work.Y, y);
        AppWindow.Move(new PointInt32(x, y));
    }

    /// <summary>
    /// Use the display of the app we'll paste into — with a keyboard-triggered overlay the
    /// mouse may be sitting on a completely different monitor.
    /// </summary>
    private DisplayArea TargetDisplay()
    {
        if (_targetWindow != 0)
        {
            var id = Win32Interop.GetWindowIdFromWindow(_targetWindow);
            var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest);
            if (area is not null)
                return area;
        }
        if (GetCursorPos(out var pt))
            return DisplayArea.GetFromPoint(new PointInt32(pt.X, pt.Y), DisplayAreaFallback.Primary);
        return DisplayArea.Primary;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}
