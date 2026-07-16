using System.Diagnostics;
using static Past.Infrastructure.Interop.NativeMethods;

namespace Past.Infrastructure.Interop;

/// <summary>Foreground window handle + owning process name.</summary>
public static class ForegroundApp
{
    public static nint GetWindow() => GetForegroundWindow();

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
