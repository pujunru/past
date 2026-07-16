using System.Collections.Concurrent;
using static Past.Infrastructure.Interop.NativeMethods;

namespace Past.Infrastructure.Interop;

/// <summary>
/// A hidden message-only window running its own message pump on a dedicated
/// background thread. Clipboard-format-listener and hotkey messages are delivered
/// here, independent of the WinUI dispatcher, and surfaced via <see cref="MessageReceived"/>.
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private readonly string _className = "PastMessageWindow_" + Guid.NewGuid().ToString("N");
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly WndProc _wndProcDelegate; // held to prevent GC of the callback
    private readonly ConcurrentQueue<Action> _pending = new();
    private Thread? _thread;
    private nint _hwnd;

    /// <summary>Raised on the pump thread for each message. Args: (msg, wParam, lParam).</summary>
    public event Action<uint, nint, nint>? MessageReceived;

    public nint Handle => _hwnd;

    public MessageWindow() => _wndProcDelegate = WndProcImpl;

    public void Start()
    {
        if (_thread is not null)
            return;

        _thread = new Thread(PumpThread)
        {
            IsBackground = true,
            Name = "PastMessagePump",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void PumpThread()
    {
        var hInstance = GetModuleHandleW(null);
        var wc = new WNDCLASS
        {
            lpfnWndProc = _wndProcDelegate,
            hInstance = hInstance,
            lpszClassName = _className,
        };
        RegisterClassW(ref wc);

        _hwnd = CreateWindowExW(0, _className, "Past", 0, 0, 0, 0, 0,
            HWND_MESSAGE, 0, hInstance, 0);
        _ready.Set();

        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    /// <summary>
    /// Run <paramref name="action"/> on the pump thread and block until it finishes.
    /// Required for APIs with thread affinity to the owning window — notably
    /// RegisterHotKey, which fails with ERROR_WINDOW_OF_OTHER_THREAD (1408) otherwise.
    /// </summary>
    public void Invoke(Action action)
    {
        if (_thread is null || _hwnd == 0)
            throw new InvalidOperationException("MessageWindow has not been started.");

        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
        {
            action();
            return;
        }

        using var done = new ManualResetEventSlim(false);
        Exception? error = null;
        _pending.Enqueue(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });
        PostMessageW(_hwnd, WM_APP_INVOKE, 0, 0);
        done.Wait(TimeSpan.FromSeconds(5));
        if (error is not null)
            throw error;
    }

    private nint WndProcImpl(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_APP_INVOKE)
        {
            while (_pending.TryDequeue(out var work))
                work();
            return 0;
        }
        if (msg == WM_APP_STOP)
        {
            DestroyWindow(hWnd);
            return 0;
        }
        if (msg == WM_DESTROY)
        {
            PostQuitMessageSafe();
            return 0;
        }

        MessageReceived?.Invoke(msg, wParam, lParam);
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void PostQuitMessageSafe()
    {
        // GetMessage returns 0 on WM_QUIT; posting a quit via PostQuitMessage.
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern void PostQuitMessage(int nExitCode);
        PostQuitMessage(0);
    }

    public void Dispose()
    {
        if (_hwnd != 0)
            PostMessageW(_hwnd, WM_APP_STOP, 0, 0);
        _thread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
