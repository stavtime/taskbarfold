using System.Runtime.InteropServices;
using TaskbarFold.Core.Models;

namespace TaskbarFold.Core.Interop;

public interface ITaskbarInfoProvider
{
    /// <summary>Describes the taskbar on the monitor containing the given screen point.</summary>
    TaskbarLayout GetLayoutAt(int screenX, int screenY);

    /// <summary>Current cursor position in physical screen pixels.</summary>
    (int X, int Y) GetCursorPosition();
}

/// <summary>
/// Works out which edge the taskbar occupies on a specific monitor.
///
/// <c>ABM_GETTASKBARPOS</c> only ever describes the primary taskbar, which is why
/// taskbar-groups put flyouts on the wrong screen. The reliable signal per monitor is
/// the difference between the monitor rectangle and its work area: whichever side is
/// inset is where the bar sits. The AppBar call is used only as a fallback, for the
/// auto-hide case where the work area equals the full monitor.
/// </summary>
public sealed class TaskbarInfo : ITaskbarInfoProvider
{
    public static TaskbarInfo Instance { get; } = new();

    public (int X, int Y) GetCursorPosition() =>
        NativeMethods.GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);

    public TaskbarLayout GetLayoutAt(int screenX, int screenY)
    {
        var monitor = NativeMethods.MonitorFromPoint(
            new POINT { X = screenX, Y = screenY }, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return Fallback(screenX, screenY);
        }

        var monitorBounds = ToPixelRect(info.rcMonitor);
        var workArea = ToPixelRect(info.rcWork);
        var dpi = GetDpi(monitor);
        var autoHide = IsAutoHide();

        var inferred = InferFromWorkArea(monitorBounds, workArea);
        if (inferred is not null)
        {
            return new TaskbarLayout(inferred.Value.Edge, inferred.Value.Bounds, monitorBounds, workArea, autoHide, dpi);
        }

        // Work area fills the monitor: either the taskbar auto-hides, or this monitor
        // has no taskbar at all. Borrow the primary taskbar's edge and thickness so the
        // flyout still appears where the bar will slide out.
        var (edge, thickness) = GetPrimaryTaskbarShape(dpi);
        var bounds = EdgeStrip(monitorBounds, edge, thickness);

        return new TaskbarLayout(edge, bounds, monitorBounds, workArea, autoHide, dpi);
    }

    private static (TaskbarEdge Edge, PixelRect Bounds)? InferFromWorkArea(PixelRect monitor, PixelRect work)
    {
        var left = work.Left - monitor.Left;
        var top = work.Top - monitor.Top;
        var right = monitor.Right - work.Right;
        var bottom = monitor.Bottom - work.Bottom;

        var largest = Math.Max(Math.Max(left, top), Math.Max(right, bottom));
        if (largest <= 0)
        {
            return null;
        }

        if (largest == bottom)
        {
            return (TaskbarEdge.Bottom, new PixelRect(monitor.Left, work.Bottom, monitor.Right, monitor.Bottom));
        }

        if (largest == top)
        {
            return (TaskbarEdge.Top, new PixelRect(monitor.Left, monitor.Top, monitor.Right, work.Top));
        }

        if (largest == left)
        {
            return (TaskbarEdge.Left, new PixelRect(monitor.Left, monitor.Top, work.Left, monitor.Bottom));
        }

        return (TaskbarEdge.Right, new PixelRect(work.Right, monitor.Top, monitor.Right, monitor.Bottom));
    }

    private static (TaskbarEdge Edge, int Thickness) GetPrimaryTaskbarShape(uint dpi)
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };

        if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref data) != 0)
        {
            var rect = ToPixelRect(data.rc);
            var edge = data.uEdge switch
            {
                NativeMethods.ABE_LEFT => TaskbarEdge.Left,
                NativeMethods.ABE_TOP => TaskbarEdge.Top,
                NativeMethods.ABE_RIGHT => TaskbarEdge.Right,
                _ => TaskbarEdge.Bottom,
            };

            var thickness = edge is TaskbarEdge.Left or TaskbarEdge.Right ? rect.Width : rect.Height;
            if (thickness > 0)
            {
                return (edge, thickness);
            }
        }

        // Default Windows 11 taskbar height at 100% scaling.
        return (TaskbarEdge.Bottom, (int)Math.Round(48 * (dpi / 96.0)));
    }

    private static PixelRect EdgeStrip(PixelRect monitor, TaskbarEdge edge, int thickness) => edge switch
    {
        TaskbarEdge.Left => new PixelRect(monitor.Left, monitor.Top, monitor.Left + thickness, monitor.Bottom),
        TaskbarEdge.Top => new PixelRect(monitor.Left, monitor.Top, monitor.Right, monitor.Top + thickness),
        TaskbarEdge.Right => new PixelRect(monitor.Right - thickness, monitor.Top, monitor.Right, monitor.Bottom),
        _ => new PixelRect(monitor.Left, monitor.Bottom - thickness, monitor.Right, monitor.Bottom),
    };

    private static bool IsAutoHide()
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        var state = (int)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data);
        return (state & NativeMethods.ABS_AUTOHIDE) != 0;
    }

    private static uint GetDpi(nint monitor)
    {
        try
        {
            // MDT_EFFECTIVE_DPI = 0
            if (NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX;
            }
        }
        catch (DllNotFoundException)
        {
            // shcore.dll is absent on pre-8.1 systems, which are out of scope anyway.
        }
        catch (EntryPointNotFoundException)
        {
        }

        return 96;
    }

    private TaskbarLayout Fallback(int screenX, int screenY)
    {
        var (edge, thickness) = GetPrimaryTaskbarShape(96);
        var monitor = new PixelRect(screenX - 960, screenY - 540, screenX + 960, screenY + 540);
        return new TaskbarLayout(
            edge, EdgeStrip(monitor, edge, thickness), monitor, monitor, IsAutoHide(), 96);
    }

    private static PixelRect ToPixelRect(RECT rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
