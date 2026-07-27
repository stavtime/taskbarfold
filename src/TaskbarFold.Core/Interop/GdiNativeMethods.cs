using System.Runtime.InteropServices;

namespace TaskbarFold.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAP
{
    public int bmType;
    public int bmWidth;
    public int bmHeight;
    public int bmWidthBytes;
    public ushort bmPlanes;
    public ushort bmBitsPixel;
    public nint bmBits;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

internal static class GdiNativeMethods
{
    internal const uint BI_RGB = 0;
    internal const uint DIB_RGB_COLORS = 0;

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    internal static extern int GetObject(nint hgdiobj, int cbBuffer, ref BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    internal static extern int GetDIBits(
        nint hdc,
        nint hbm,
        uint start,
        uint cLines,
        [Out] byte[]? lpvBits,
        ref BITMAPINFOHEADER lpbmi,
        uint usage);
}
