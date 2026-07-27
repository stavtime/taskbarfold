namespace TaskbarFold.Core.Models;

/// <summary>Screen edge the taskbar is docked to.</summary>
public enum TaskbarEdge
{
    Left,
    Top,
    Right,
    Bottom,
}

/// <summary>
/// A rectangle in physical screen pixels. Deliberately not <c>System.Windows.Rect</c>:
/// the domain layer must not pull in WindowsBase.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public static PixelRect FromSize(int left, int top, int width, int height) =>
        new(left, top, left + width, top + height);
}

/// <summary>
/// Everything the flyout needs in order to place itself against the taskbar of one
/// specific monitor: which edge it sits on, how thick it is, whether it auto-hides,
/// and the DPI of that monitor.
/// </summary>
/// <param name="Edge">Edge the taskbar is docked to.</param>
/// <param name="TaskbarBounds">Taskbar rectangle in physical pixels.</param>
/// <param name="MonitorBounds">Full monitor rectangle in physical pixels.</param>
/// <param name="WorkArea">Monitor area excluding docked app bars.</param>
/// <param name="AutoHide">True when the taskbar auto-hides and therefore may not occupy its bounds.</param>
/// <param name="Dpi">Effective DPI of the monitor (96 = 100%).</param>
public sealed record TaskbarLayout(
    TaskbarEdge Edge,
    PixelRect TaskbarBounds,
    PixelRect MonitorBounds,
    PixelRect WorkArea,
    bool AutoHide,
    uint Dpi)
{
    public double Scale => Dpi / 96.0;

    /// <summary>
    /// Taskbar thickness in physical pixels. An auto-hidden taskbar reports a sliver
    /// of a few pixels, so a sane minimum is substituted to keep the gap stable.
    /// </summary>
    public int Thickness
    {
        get
        {
            var raw = Edge is TaskbarEdge.Left or TaskbarEdge.Right
                ? TaskbarBounds.Width
                : TaskbarBounds.Height;
            var minimum = (int)Math.Round(2 * Scale);
            return Math.Max(raw, minimum);
        }
    }

    public bool IsHorizontal => Edge is TaskbarEdge.Top or TaskbarEdge.Bottom;
}
