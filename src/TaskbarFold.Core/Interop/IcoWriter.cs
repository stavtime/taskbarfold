using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TaskbarFold.Core.Interop;

/// <summary>
/// Builds a multi-resolution .ico from an arbitrary image. The taskbar, alt-tab and
/// the properties dialog all ask for different sizes; shipping a single 48px frame
/// makes the pinned group look blurry in half of them.
///
/// Frames at 128px and above are stored as PNG to keep the file small. Everything
/// smaller is stored as a classic 32bpp DIB, because <c>System.Drawing.Icon</c> — still
/// on the path of several tray and shell consumers — cannot decode PNG frames.
/// </summary>
public static class IcoWriter
{
    /// <summary>Sizes Windows actually asks for, smallest first.</summary>
    public static readonly int[] StandardSizes = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>At and above this edge length a frame is PNG-compressed.</summary>
    private const int PngThreshold = 128;

    public static void WriteFromImageFile(string sourceImagePath, string destinationIcoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationIcoPath);

        using var source = new Bitmap(sourceImagePath);
        WriteFromBitmap(source, destinationIcoPath);
    }

    public static void WriteFromPng(byte[] pngBytes, string destinationIcoPath)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);

        using var stream = new MemoryStream(pngBytes, writable: false);
        using var source = new Bitmap(stream);
        WriteFromBitmap(source, destinationIcoPath);
    }

    public static void WriteFromBitmap(Bitmap source, string destinationIcoPath, IReadOnlyList<int>? sizes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationIcoPath);

        var bytes = Build(source, sizes);

        var directory = Path.GetDirectoryName(destinationIcoPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temporary file first: a pinned .lnk pointing at a half-written
        // .ico shows a blank tile until the shell icon cache is cleared.
        var temp = destinationIcoPath + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, destinationIcoPath, overwrite: true);
    }

    /// <summary>
    /// Produces the .ico as bytes. Exposed separately so a caller can compare against
    /// what is already on disk and skip rewriting a file the shell has cached.
    /// </summary>
    public static byte[] Build(Bitmap source, IReadOnlyList<int>? sizes = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var frames = BuildFrames(source, sizes ?? StandardSizes);
        if (frames.Count == 0)
        {
            throw new InvalidOperationException("No icon frames could be produced from the source image.");
        }

        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);              // reserved
            writer.Write((ushort)1);              // type: icon
            writer.Write((ushort)frames.Count);

            const int headerSize = 6;
            const int entrySize = 16;
            var offset = headerSize + (entrySize * frames.Count);

            foreach (var frame in frames)
            {
                // 256 is encoded as 0, because the field is a single byte.
                var dimension = (byte)(frame.Size >= 256 ? 0 : frame.Size);
                writer.Write(dimension);
                writer.Write(dimension);
                writer.Write((byte)0);            // palette size, 0 for truecolor
                writer.Write((byte)0);            // reserved
                writer.Write((ushort)1);          // color planes
                writer.Write((ushort)32);         // bits per pixel
                writer.Write(frame.Data.Length);
                writer.Write(offset);
                offset += frame.Data.Length;
            }

            foreach (var frame in frames)
            {
                writer.Write(frame.Data);
            }
        }

        return output.ToArray();
    }

    private static List<IcoFrame> BuildFrames(Bitmap source, IReadOnlyList<int> requested)
    {
        var frames = new List<IcoFrame>(requested.Count);

        foreach (var size in requested.Distinct().OrderBy(s => s))
        {
            if (size is < 1 or > 256)
            {
                continue;
            }

            using var scaled = IconExtractor.Resize(source, size);
            frames.Add(new IcoFrame(size, size >= PngThreshold ? EncodePng(scaled) : EncodeDib(scaled)));
        }

        return frames;
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var buffer = new MemoryStream();
        bitmap.Save(buffer, ImageFormat.Png);
        return buffer.ToArray();
    }

    /// <summary>
    /// Encodes one frame the way a pre-Vista icon does: a BITMAPINFOHEADER whose height
    /// covers both the colour bitmap and the AND mask, then bottom-up BGRA rows, then the
    /// mask itself. The mask is left zeroed — the 32bpp alpha channel carries the shape —
    /// but it has to be present or the frame is rejected.
    /// </summary>
    private static byte[] EncodeDib(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;

        var pixels = new byte[width * height * 4];
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(data.Scan0 + (row * data.Stride), pixels, row * width * 4, width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        var maskStride = ((width + 31) / 32) * 4;
        var headerSize = Marshal.SizeOf<BITMAPINFOHEADER>();
        var result = new byte[headerSize + pixels.Length + (maskStride * height)];

        using (var stream = new MemoryStream(result))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((uint)headerSize);
            writer.Write(width);
            writer.Write(height * 2);             // colour bitmap plus AND mask
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0u);                     // BI_RGB
            writer.Write((uint)pixels.Length);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0u);
            writer.Write(0u);

            // DIB rows run bottom-up.
            for (var row = height - 1; row >= 0; row--)
            {
                writer.Write(pixels, row * width * 4, width * 4);
            }
        }

        return result;
    }

    private readonly record struct IcoFrame(int Size, byte[] Data);
}
