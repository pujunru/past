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

    /// <summary>
    /// Names of the formats currently on the clipboard, for diagnostics only.
    /// Lets an unsupported source identify itself instead of failing silently.
    /// </summary>
    public static string DescribeFormats(nint owner)
    {
        var names = new List<string>();
        WithClipboard(owner, () =>
        {
            uint format = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                var sb = new System.Text.StringBuilder(128);
                var len = GetClipboardFormatNameW(format, sb, sb.Capacity);
                names.Add(len > 0 ? $"{sb} ({format})" : StandardName(format));
            }
            return null;
        });
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private static string StandardName(uint format) => format switch
    {
        1 => "CF_TEXT",
        2 => "CF_BITMAP",
        CF_UNICODETEXT => "CF_UNICODETEXT",
        CF_DIB => "CF_DIB",
        CF_DIBV5 => "CF_DIBV5",
        15 => "CF_HDROP",
        _ => $"format {format}",
    };

    /// <summary>Read an arbitrary clipboard format as raw bytes (used for CF_DIB).</summary>
    public static byte[]? TryGetBytes(nint owner, uint format)
    {
        if (!IsClipboardFormatAvailable(format))
            return null;

        byte[]? result = null;
        WithClipboard(owner, () =>
        {
            var handle = GetClipboardData(format);
            if (handle == 0)
                return null;

            var size = (int)GlobalSize(handle);
            if (size <= 0)
                return null;

            var ptr = GlobalLock(handle);
            if (ptr == 0)
                return null;
            try
            {
                var buffer = new byte[size];
                Marshal.Copy(ptr, buffer, 0, size);
                result = buffer;
            }
            finally
            {
                GlobalUnlock(handle);
            }
            return null;
        });
        return result;
    }

    /// <summary>Put raw bytes on the clipboard under <paramref name="format"/> (used for CF_DIB).</summary>
    public static void SetBytes(nint owner, uint format, byte[] bytes)
    {
        WithClipboard(owner, () =>
        {
            EmptyClipboard();
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes.Length);
            var target = GlobalLock(hGlobal);
            try
            {
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }
            SetClipboardData(format, hGlobal); // ownership transfers to the system
            return null;
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
