using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TaskbarFold.App.Services;

/// <summary>
/// Applies the Windows 11 acrylic backdrop and rounded corners to a window.
///
/// <c>DWMWA_SYSTEMBACKDROP_TYPE</c> only exists from Windows 11 build 22621. On anything
/// older the flyout falls back to a solid colour with layered-window transparency, which
/// is why <see cref="IsSupported"/> has to be consulted before the window is constructed:
/// <c>AllowsTransparency</c> cannot be changed once the handle exists, and a layered
/// window suppresses the DWM backdrop entirely.
/// </summary>
public static class SystemBackdrop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMSBT_TRANSIENTWINDOW = 3; // acrylic, the flyout material
    private const int DWMWCP_ROUND = 2;

    private static readonly Version MinimumBuild = new(10, 0, 22621, 0);

    public static bool IsSupported { get; } = Environment.OSVersion.Version >= MinimumBuild;

    /// <summary>
    /// Enables the acrylic material. The window's own background must be transparent and
    /// the client area extended, otherwise WPF paints over the backdrop the DWM composes.
    /// </summary>
    public static bool TryApplyAcrylic(Window window, bool dark)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!IsSupported)
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return false;
        }

        try
        {
            var source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(handle, ref margins);

            SetAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW);
            ApplyRoundedCorners(window);
            SetDarkMode(window, dark);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static void ApplyRoundedCorners(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != nint.Zero)
        {
            SetAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        }
    }

    /// <summary>Switches the non-client area (title bar, border) to the dark palette.</summary>
    public static void SetDarkMode(Window window, bool dark)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != nint.Zero)
        {
            SetAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
        }
    }

    private static void SetAttribute(nint handle, int attribute, int value)
    {
        try
        {
            DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows simply ignores the attribute; there is nothing to recover from.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS pMarInset);
}
