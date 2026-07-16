using System.Diagnostics;
using static Past.Infrastructure.Interop.NativeMethods;

namespace Past.Infrastructure.Interop;

/// <summary>Foreground window handle + owning process name, and forcing foreground.</summary>
public static class ForegroundApp
{
    public static nint GetWindow() => GetForegroundWindow();

    /// <summary>
    /// Bring <paramref name="hWnd"/> to the foreground WITH keyboard focus.
    /// <para>
    /// A bare SetForegroundWindow is routinely refused for a background process: Windows
    /// shows the window but withholds focus, so keys go to the old app and the overlay
    /// ignores Esc/arrows. Temporarily attaching our input queue to the current foreground
    /// thread satisfies the foreground-lock rules and gets focus handed over properly.
    /// </para>
    /// Must be called from the thread owning <paramref name="hWnd"/>.
    /// </summary>
    public static bool ForceForeground(nint hWnd)
    {
        if (hWnd == 0)
            return false;

        var foreground = GetForegroundWindow();
        if (foreground == hWnd)
            return true;

        var ourThread = GetCurrentThreadId();
        var foregroundThread = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, out _);

        var attached = foregroundThread != 0
                       && foregroundThread != ourThread
                       && AttachThreadInput(ourThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hWnd);
            var ok = SetForegroundWindow(hWnd);
            SetFocus(hWnd);
            return ok;
        }
        finally
        {
            if (attached)
                AttachThreadInput(ourThread, foregroundThread, false);
        }
    }

    public static string? GetProcessName(nint hWnd)
    {
        if (hWnd == 0)
            return null;
        GetWindowThreadProcessId(hWnd, out var pid);
        if (pid == 0)
            return null;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return null; // process may have exited or be inaccessible
        }
    }
}
