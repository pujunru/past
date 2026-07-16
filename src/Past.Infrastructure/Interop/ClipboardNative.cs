using System.Runtime.InteropServices;
using static Past.Infrastructure.Interop.NativeMethods;

namespace Past.Infrastructure.Interop;

/// <summary>Low-level CF_UNICODETEXT read/write with retry for clipboard-lock contention.</summary>
internal static class ClipboardNative
{
    private const int MaxAttempts = 10;

    public static string? TryGetText(nint owner)
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
            return null;

        return WithClipboard(owner, static () =>
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == 0)
                return null;
            var ptr = GlobalLock(handle);
            if (ptr == 0)
                return null;
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        });
    }

    public static void SetText(nint owner, string text)
    {
        WithClipboard(owner, () =>
        {
            EmptyClipboard();
            var chars = text.ToCharArray();
            var size = (nuint)((chars.Length + 1) * 2);
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, size);
            var target = GlobalLock(hGlobal);
            try
            {
                Marshal.Copy(chars, 0, target, chars.Length);
                Marshal.WriteInt16(target, chars.Length * 2, 0); // null terminator
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }
            SetClipboardData(CF_UNICODETEXT, hGlobal); // ownership transfers to the system
            return (string?)null;
        });
    }

    private static string? WithClipboard(nint owner, Func<string?> action)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (OpenClipboard(owner))
            {
                try
                {
                    return action();
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(15); // another process holds the clipboard; back off briefly
        }
        return null;
    }
}
