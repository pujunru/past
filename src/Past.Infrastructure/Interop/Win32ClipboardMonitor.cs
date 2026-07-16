using Past.Core;
using Past.Services;
using static Past.Infrastructure.Interop.NativeMethods;

namespace Past.Infrastructure.Interop;

/// <summary>
/// <see cref="IClipboardMonitor"/> over the Win32 clipboard-format listener.
/// Event-driven (no polling): each WM_CLIPBOARDUPDATE reads CF_UNICODETEXT and,
/// unless it was our own write, raises <see cref="ClipCaptured"/> with the source app.
/// </summary>
public sealed class Win32ClipboardMonitor : IClipboardMonitor
{
    private readonly MessageWindow _window;
    private readonly SelfCopyGuard _guard;
    private bool _listening;

    public event EventHandler<ClipDraft>? ClipCaptured;

    /// <summary>When false, updates are ignored (P0 pause hook; wired up in P1 UI).</summary>
    public bool CaptureEnabled { get; set; } = true;

    public Win32ClipboardMonitor(MessageWindow window, SelfCopyGuard guard)
    {
        _window = window;
        _guard = guard;
        _window.MessageReceived += OnMessage;
    }

    public void Start()
    {
        if (_listening)
            return;
        AddClipboardFormatListener(_window.Handle);
        _listening = true;
    }

    public void Stop()
    {
        if (!_listening)
            return;
        RemoveClipboardFormatListener(_window.Handle);
        _listening = false;
    }

    private void OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg != WM_CLIPBOARDUPDATE || !CaptureEnabled)
            return;

        var text = ClipboardNative.TryGetText(_window.Handle);
        if (string.IsNullOrEmpty(text))
            return;

        if (_guard.ShouldIgnore(text))
            return; // our own paste/copy echoed back

        var fg = ForegroundApp.GetWindow();
        var app = ForegroundApp.GetProcessName(fg);
        ClipCaptured?.Invoke(this, new ClipDraft(text, app));
    }

    public void Dispose()
    {
        Stop();
        _window.MessageReceived -= OnMessage;
    }
}
