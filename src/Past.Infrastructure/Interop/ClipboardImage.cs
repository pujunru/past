using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Past.Core;

namespace Past.Infrastructure.Interop;

/// <summary>
/// Converts between the clipboard's CF_DIB format and PNG.
/// <para>
/// We store PNG rather than the raw DIB: it is typically far smaller, and it is directly
/// renderable by the UI. The DIB is rebuilt on paste.
/// </para>
/// </summary>
internal static class ClipboardImage
{
    /// <summary>Decoded clipboard bitmap, encoded for storage.</summary>
    internal sealed record Decoded(byte[] Png, byte[]? Thumbnail, int Width, int Height);

    /// <summary>Read CF_DIB off the clipboard and encode it as PNG (+ thumbnail).</summary>
    public static Decoded? TryRead(nint owner, Action<string>? log = null)
    {
        var dib = ClipboardNative.TryGetBytes(owner, NativeMethods.CF_DIB);
        if (dib is null || dib.Length < Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>())
        {
            log?.Invoke($"image: no usable CF_DIB (bytes={dib?.Length ?? -1})");
            return null;
        }

        try
        {
            using var bitmap = FromDib(dib);
            if (bitmap is null)
            {
                log?.Invoke($"image: DIB decode returned null (dibBytes={dib.Length})");
                return null;
            }

            var png = ToPng(bitmap);

            // Very large images are still captured and still paste; we just skip the
            // thumbnail, since decoding/scaling them costs real time on every copy.
            byte[]? thumb = null;
            if (png.Length <= ClipLimits.MaxThumbnailSourceBytes)
                thumb = MakeThumbnail(bitmap);

            log?.Invoke($"image: captured {bitmap.Width}x{bitmap.Height} png={png.Length}B thumb={(thumb?.Length ?? 0)}B");
            return new Decoded(png, thumb, bitmap.Width, bitmap.Height);
        }
        catch (Exception ex)
        {
            // A malformed or exotic bitmap must never take the capture pipeline down,
            // but swallowing it silently hides real bugs - say what happened.
            log?.Invoke($"image: decode FAILED {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Rebuild a packed DIB (header + palette + pixels) suitable for CF_DIB.</summary>
    public static byte[] ToDib(byte[] png)
    {
        using var ms = new MemoryStream(png);
        using var src = new Bitmap(ms);
        using var bmp = src.PixelFormat == PixelFormat.Format32bppArgb
            ? (Bitmap)src.Clone()
            : src.Clone(new Rectangle(0, 0, src.Width, src.Height), PixelFormat.Format32bppArgb);

        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = bmp.Width * 4;
            var pixels = new byte[stride * bmp.Height];

            // DIBs are bottom-up: copy rows in reverse.
            for (var y = 0; y < bmp.Height; y++)
            {
                var src2 = data.Scan0 + (y * data.Stride);
                var dst = (bmp.Height - 1 - y) * stride;
                Marshal.Copy(src2, pixels, dst, stride);
            }

            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = bmp.Width,
                biHeight = bmp.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
                biSizeImage = (uint)pixels.Length,
            };

            var headerSize = (int)header.biSize;
            var buffer = new byte[headerSize + pixels.Length];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(header, handle.AddrOfPinnedObject(), false);
            }
            finally
            {
                handle.Free();
            }
            Buffer.BlockCopy(pixels, 0, buffer, headerSize, pixels.Length);
            return buffer;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static Bitmap? FromDib(byte[] dib)
    {
        var header = BytesToHeader(dib);
        if (header.biWidth <= 0 || header.biHeight == 0 || header.biBitCount != 32)
            return FromDibViaGdi(dib, header);

        var width = header.biWidth;
        var height = Math.Abs(header.biHeight);
        var topDown = header.biHeight < 0;
        var stride = width * 4;
        var offset = (int)header.biSize;
        if (dib.Length < offset + (stride * height))
            return null;

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < height; y++)
            {
                var srcRow = topDown ? y : height - 1 - y;
                Marshal.Copy(dib, offset + (srcRow * stride), data.Scan0 + (y * data.Stride), stride);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    /// <summary>Fallback for palette/16/24-bit DIBs: let GDI+ do the conversion.</summary>
    private static Bitmap? FromDibViaGdi(byte[] dib, NativeMethods.BITMAPINFOHEADER header)
    {
        // Prepend a BITMAPFILEHEADER so the bytes become a loadable .bmp stream.
        const int fileHeaderSize = 14;
        var paletteSize = (int)header.biClrUsed * 4;
        if (paletteSize == 0 && header.biBitCount <= 8)
            paletteSize = (1 << header.biBitCount) * 4;

        var pixelOffset = fileHeaderSize + (int)header.biSize + paletteSize;
        var buffer = new byte[fileHeaderSize + dib.Length];
        using var ms = new MemoryStream(buffer);
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            bw.Write((byte)'B');
            bw.Write((byte)'M');
            bw.Write(buffer.Length);
            bw.Write((short)0);
            bw.Write((short)0);
            bw.Write(pixelOffset);
        }
        Buffer.BlockCopy(dib, 0, buffer, fileHeaderSize, dib.Length);

        using var load = new MemoryStream(buffer);
        try
        {
            using var loaded = new Bitmap(load);
            return loaded.Clone(new Rectangle(0, 0, loaded.Width, loaded.Height), PixelFormat.Format32bppArgb);
        }
        catch
        {
            return null;
        }
    }

    private static NativeMethods.BITMAPINFOHEADER BytesToHeader(byte[] dib)
    {
        var handle = GCHandle.Alloc(dib, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<NativeMethods.BITMAPINFOHEADER>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private static byte[] ToPng(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] MakeThumbnail(Bitmap source)
    {
        var max = ClipLimits.ThumbnailMaxEdge;
        var scale = Math.Min(1.0, (double)max / Math.Max(source.Width, source.Height));
        var w = Math.Max(1, (int)(source.Width * scale));
        var h = Math.Max(1, (int)(source.Height * scale));

        using var thumb = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(thumb))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, w, h);
        }
        return ToPng(thumb);
    }
}
