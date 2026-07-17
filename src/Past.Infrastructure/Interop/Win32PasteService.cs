using Past.Services;
using static Past.Infrastructure.Interop.NativeMethods;

namespace Past.Infrastructure.Interop;

/// <summary>
/// <see cref="IPasteService"/>: put text on the clipboard and paste it into the
/// previously focused window by restoring focus and synthesizing Ctrl+V.
/// </summary>
public sealed class Win32PasteService : IPasteService
{
    private readonly MessageWindow _window;
    private readonly SelfCopyGuard _guard;
    private readonly Action<string>? _log;

    public Win32PasteService(MessageWindow window, SelfCopyGuard guard, Action<string>? log = null)
    {
        _window = window;
        _guard = guard;
        _log = log;
    }

    public void SetClipboardText(string text)
    {
        _guard.MarkWritten(text); // suppress re-capture of our own write
        ClipboardNative.SetText(_window.Handle, text);
    }

    public void SetClipboardImage(byte[] png)
    {
        _guard.MarkImageWritten(png);
        // We store PNG, but CF_DIB is what apps actually accept when pasting a bitmap.
        ClipboardNative.SetBytes(_window.Handle, CF_DIB, ClipboardImage.ToDib(png));
    }

    public void PasteInto(nint targetWindow)
    {
        // Same foreground-lock problem as showing the overlay: a plain SetForegroundWindow
        // is often refused, and the keystroke then lands in the wrong app.
        var setOk = ForegroundApp.ForceForeground(targetWindow);

        // Wait until the target is really foreground instead of guessing with a fixed sleep;
        // a too-short delay made the paste land nowhere intermittently.
        var settledMs = WaitForForeground(targetWindow, timeoutMs: 500);

        // The hotkey is Win+Alt+V. If those are still physically held, our synthetic Ctrl+V
        // arrives as Win+Alt+Ctrl+V and the target ignores it — the main cause of "sometimes
        // it pastes, sometimes it doesn't". Give the user a moment to let go, then force it.
        var held = ReleaseHeldModifiers(graceMs: 250);

        var sent = SendCtrlV();
        var actual = GetForegroundWindow();
        _log?.Invoke($"pasteInto target=0x{targetWindow:X} setForeground={setOk} settledMs={settledMs} " +
                     $"heldModifiers=[{held}] actualForeground=0x{actual:X} inputsSent={sent}");
    }

    /// <summary>Poll until <paramref name="target"/> is foreground. Returns ms waited, or -1 on timeout.</summary>
    private static int WaitForForeground(nint target, int timeoutMs)
    {
        if (target == 0)
            return -1;

        for (var waited = 0; waited < timeoutMs; waited += 10)
        {
            if (GetForegroundWindow() == target)
                return waited;
            Thread.Sleep(10);
        }
        return -1;
    }

    /// <summary>
    /// Wait briefly for the user to release hotkey modifiers, then force-release any still
    /// down. Returns which ones we had to force. Releasing Win here is safe: it was already
    /// consumed by the hotkey's V press, so it won't pop the Start menu.
    /// </summary>
    private static string ReleaseHeldModifiers(int graceMs)
    {
        ReadOnlySpan<ushort> mods = [VK_LWIN, VK_RWIN, VK_MENU, VK_CONTROL, VK_SHIFT];

        for (var waited = 0; waited < graceMs; waited += 10)
        {
            if (!AnyDown(mods))
                return "none";
            Thread.Sleep(10);
        }

        var forced = new List<string>();
        var ups = new List<INPUT>();
        foreach (var vk in mods)
        {
            if (!IsDown(vk))
                continue;
            forced.Add(NameOf(vk));
            ups.Add(KeyInput(vk, down: false));
        }

        if (ups.Count > 0)
        {
            SendInput((uint)ups.Count, ups.ToArray(), System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
            Thread.Sleep(20); // let the release register before Ctrl+V
        }
        return forced.Count == 0 ? "none" : string.Join("+", forced);
    }

    private static bool AnyDown(ReadOnlySpan<ushort> vks)
    {
        foreach (var vk in vks)
        {
            if (IsDown(vk))
                return true;
        }
        return false;
    }

    private static bool IsDown(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static string NameOf(ushort vk) => vk switch
    {
        VK_LWIN or VK_RWIN => "Win",
        VK_MENU => "Alt",
        VK_CONTROL => "Ctrl",
        VK_SHIFT => "Shift",
        _ => "0x" + vk.ToString("X"),
    };

    private static uint SendCtrlV()
    {
        var inputs = new[]
        {
            KeyInput(VK_CONTROL, down: true),
            KeyInput(VK_V, down: true),
            KeyInput(VK_V, down: false),
            KeyInput(VK_CONTROL, down: false),
        };
        return SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyInput(ushort vk, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = down ? 0 : KEYEVENTF_KEYUP,
            },
        },
    };
}
