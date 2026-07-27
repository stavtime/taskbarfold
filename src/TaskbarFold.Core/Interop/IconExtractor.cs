using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TaskbarFold.Core.Interop;

/// <summary>Bitmap handed back to the caller as raw pixels, so no GDI handle escapes this layer.</summary>
/// <param name="Width">Pixel width.</param>
/// <param name="Height">Pixel height.</param>
/// <param name="PngBytes">The image encoded as PNG.</param>
public sealed record IconImage(int Width, int Height, byte[] PngBytes);

public interface IIconExtractor
{
    /// <summary>
    /// Renders the icon of a file system path at the requested edge length.
    /// Returns null when the shell has nothing to offer for that path.
    /// </summary>
    IconImage? ExtractFromPath(string path, int size);

    /// <summary>Renders the tile of a packaged app identified by its AppUserModelID.</summary>
    IconImage? ExtractFromAppUserModelId(string appUserModelId, int size);
}

/// <summary>
/// Pulls icons out of the shell. The primary path is <c>IShellItemImageFactory</c>,
/// which returns the largest asset the shell has (including 256px Store logos and
/// packaged-app tiles) and works for .exe, .lnk, documents and AppsFolder entries
/// alike. <c>ExtractIconEx</c> is kept as a fallback for the rare path the shell
/// declines to render.
/// </summary>
public sealed class IconExtractor : IIconExtractor
{
    /// <summary>Prefix that turns an AUMID into a shell parsing name.</summary>
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    public IconImage? ExtractFromPath(string path, int size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // The legacy fallback happily returns the generic "unknown file" icon for a path
        // that does not exist. Handing that back would paint a plausible-looking tile
        // over a broken shortcut, hiding exactly what the user needs to see.
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        return Extract(path, size) ?? ExtractLegacy(path, size);
    }

    public IconImage? ExtractFromAppUserModelId(string appUserModelId, int size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        return Extract(AppsFolderPrefix + appUserModelId, size);
    }

    private static IconImage? Extract(string parsingName, int size)
    {
        nint hbitmap = 0;
        try
        {
            NativeMethods.SHCreateItemFromParsingName(
                parsingName, 0, ShellGuids.IShellItemImageFactory, out var raw);

            if (raw is not IShellItemImageFactory factory)
            {
                return null;
            }

            try
            {
                // BiggerSizeOk lets the shell hand back a larger native asset instead of
                // upscaling a small one; the caller downsamples, which looks far better.
                var hr = factory.GetImage(
                    new SIZE(size, size), SIIGBF.IconOnly | SIIGBF.BiggerSizeOk, out hbitmap);

                if (hr != 0 || hbitmap == 0)
                {
                    return null;
                }

                using var bitmap = FromHBitmapWithAlpha(hbitmap);
                if (bitmap is null)
                {
                    return null;
                }

                using var scaled = Resize(bitmap, size);
                return Encode(scaled);
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        // A missing or unparseable path surfaces as FileNotFoundException rather than
        // COMException, because PreserveSig=false maps the HRESULT to a typed exception.
        catch (Exception ex) when (ex is COMException or ArgumentException or IOException)
        {
            return null;
        }
        finally
        {
            if (hbitmap != 0)
            {
                NativeMethods.DeleteObject(hbitmap);
            }
        }
    }

    private static IconImage? ExtractLegacy(string path, int size)
    {
        var large = new nint[1];
        try
        {
            var extracted = NativeMethods.ExtractIconEx(path, 0, large, null, 1);
            var handle = extracted > 0 ? large[0] : 0;

            if (handle == 0)
            {
                var info = default(SHFILEINFO);
                var result = NativeMethods.SHGetFileInfo(
                    path,
                    NativeMethods.FILE_ATTRIBUTE_NORMAL,
                    ref info,
                    (uint)Marshal.SizeOf<SHFILEINFO>(),
                    NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON | NativeMethods.SHGFI_USEFILEATTRIBUTES);

                if (result == 0 || info.hIcon == 0)
                {
                    return null;
                }

                handle = info.hIcon;
            }

            try
            {
                using var icon = Icon.FromHandle(handle);
                using var bitmap = icon.ToBitmap();
                using var scaled = Resize(bitmap, size);
                return Encode(scaled);
            }
            finally
            {
                NativeMethods.DestroyIcon(handle);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>Image.FromHbitmap</c> drops the alpha channel, which turns every modern icon
    /// into a black box. Copying the DIB bits through <c>GetDIBits</c> preserves it.
    /// </summary>
    private static Bitmap? FromHBitmapWithAlpha(nint hbitmap)
    {
        var info = default(BITMAP);
        if (GdiNativeMethods.GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), ref info) == 0)
        {
            return null;
        }

        var width = info.bmWidth;
        var height = Math.Abs(info.bmHeight);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // negative requests a top-down buffer
            biPlanes = 1,
            biBitCount = 32,
            biCompression = GdiNativeMethods.BI_RGB,
        };

        var buffer = new byte[checked(width * height * 4)];
        var hdc = GdiNativeMethods.GetDC(0);
        try
        {
            var copied = GdiNativeMethods.GetDIBits(
                hdc, hbitmap, 0, (uint)height, buffer, ref header, GdiNativeMethods.DIB_RGB_COLORS);

            if (copied == 0)
            {
                return null;
            }
        }
        finally
        {
            GdiNativeMethods.ReleaseDC(0, hdc);
        }

        // A source without an alpha channel comes back fully transparent; treat that as opaque.
        var hasAlpha = false;
        for (var i = 3; i < buffer.Length; i += 4)
        {
            if (buffer[i] != 0)
            {
                hasAlpha = true;
                break;
            }
        }

        if (!hasAlpha)
        {
            for (var i = 3; i < buffer.Length; i += 4)
            {
                buffer[i] = 0xFF;
            }
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(buffer, row * width * 4, data.Scan0 + (row * data.Stride), width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    internal static Bitmap Resize(Bitmap source, int size)
    {
        if (source.Width == size && source.Height == size)
        {
            return (Bitmap)source.Clone();
        }

        var target = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(target);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        graphics.Clear(Color.Transparent);

        // Keep the aspect ratio: wide Store logos must not be stretched into a square.
        var scale = Math.Min((float)size / source.Width, (float)size / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var x = (size - width) / 2;
        var y = (size - height) / 2;

        graphics.DrawImage(source, x, y, width, height);
        return target;
    }

    private static IconImage Encode(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new IconImage(bitmap.Width, bitmap.Height, stream.ToArray());
    }
}
