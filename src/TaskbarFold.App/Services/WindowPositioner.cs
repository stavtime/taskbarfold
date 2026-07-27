using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TaskbarFold.Core.Interop;
using TaskbarFold.Core.Models;

namespace TaskbarFold.App.Services;

/// <summary>
/// Places the flyout against the taskbar on the monitor the click happened on.
///
/// Positions are computed and applied in physical pixels through SetWindowPos rather
/// than through Window.Left/Top. Under per-monitor DPI awareness WPF's logical
/// coordinates are relative to the window's current DPI context, so setting them while
/// the window is moving between monitors of different scaling lands it in the wrong
/// place — the exact class of bug this application is meant to avoid.
/// </summary>
public static class WindowPositioner
{
    /// <summary>Gap between the taskbar and the flyout, in device-independent pixels.</summary>
    public const double GapDip = 8.0;

    /// <summary>Distance kept from the screen edges when clamping, in DIP.</summary>
    private const double MarginDip = 8.0;

    public static void PositionAgainstTaskbar(Window window, TaskbarLayout layout, int cursorX, int cursorY)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(layout);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        // The window may still be sized in the DPI context of the previous monitor, so
        // ask WPF for its logical size and convert with the target monitor's scale.
        var scale = layout.Scale;
        var width = (int)Math.Ceiling(window.ActualWidth * scale);
        var height = (int)Math.Ceiling(window.ActualHeight * scale);

        if (width <= 0 || height <= 0)
        {
            width = (int)Math.Ceiling(window.Width * scale);
            height = (int)Math.Ceiling(window.Height * scale);
        }

        var gap = (int)Math.Round(GapDip * scale);
        var margin = (int)Math.Round(MarginDip * scale);
        var work = layout.WorkArea.IsEmpty ? layout.MonitorBounds : layout.WorkArea;

        // An auto-hidden taskbar leaves the work area covering the whole monitor, so the
        // gap has to be measured from the monitor edge plus the bar's own thickness.
        var reserve = layout.AutoHide ? layout.Thickness : 0;

        int left, top;

        switch (layout.Edge)
        {
            case TaskbarEdge.Bottom:
                left = cursorX - (width / 2);
                top = work.Bottom - reserve - gap - height;
                break;

            case TaskbarEdge.Top:
                left = cursorX - (width / 2);
                top = work.Top + reserve + gap;
                break;

            case TaskbarEdge.Left:
                left = work.Left + reserve + gap;
                top = cursorY - (height / 2);
                break;

            default:
                left = work.Right - reserve - gap - width;
                top = cursorY - (height / 2);
                break;
        }

        // Clamp into the monitor. Without this a group opened near a screen corner is
        // half off-screen, and on a secondary monitor it can land on the wrong display.
        var bounds = layout.MonitorBounds;
        left = Math.Clamp(left, bounds.Left + margin, Math.Max(bounds.Left + margin, bounds.Right - width - margin));
        top = Math.Clamp(top, bounds.Top + margin, Math.Max(bounds.Top + margin, bounds.Bottom - height - margin));

        SetWindowPos(handle, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>Direction the open animation slides from, given where the taskbar is.</summary>
    public static (double X, double Y) SlideOffsetFor(TaskbarEdge edge) => edge switch
    {
        TaskbarEdge.Bottom => (0, GapDip),
        TaskbarEdge.Top => (0, -GapDip),
        TaskbarEdge.Left => (-GapDip, 0),
        _ => (GapDip, 0),
    };

    private static readonly nint HWND_TOPMOST = new(-1);

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
