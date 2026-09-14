namespace Translator.Platform;

/// <summary>
/// Pure window-positioning math (unit-tested) plus thin Win32 wrappers to apply it.
/// </summary>
public static class WindowPlacement
{
    public const double GapDips = 10;
    public const double MarginDips = 12;

    /// <summary><see cref="PlaceNearAnchor(PixelSize, PixelRect, PixelRect, int, int)"/> with gap and margin scaled for the anchor's monitor.</summary>
    public static PixelPoint PlaceNearAnchor(PixelSize window, PixelRect anchor, MonitorMetrics monitor) =>
        PlaceNearAnchor(window, anchor, monitor.WorkArea, monitor.ToPixels(GapDips), monitor.ToPixels(MarginDips));

    /// <summary><see cref="PlaceAtTray(PixelSize, PixelRect?, PixelRect, TaskbarEdge, int, int)"/> with gap and margin scaled for the monitor.</summary>
    public static PixelPoint PlaceAtTray(PixelSize window, PixelRect? trayIconRect, MonitorMetrics monitor, TaskbarEdge edge) =>
        PlaceAtTray(window, trayIconRect, monitor.WorkArea, edge, monitor.ToPixels(GapDips), monitor.ToPixels(MarginDips));

    /// <summary>
    /// What a tray-anchored window is placed against: the icon, else the primary taskbar (where the notification
    /// area lives, e.g. when the icon is hidden in the overflow), and only then the cursor's monitor.
    /// </summary>
    public static PixelRect TrayReference(PixelRect? trayIconRect, PixelRect? primaryTaskbar, PixelRect cursor) =>
        trayIconRect ?? primaryTaskbar ?? cursor;

    /// <summary>
    /// Above the anchor (e.g. the text selection), below it if it doesn't fit, clamped into the
    /// work area. Mirrors macOS <c>BubblePanel.show(near:)</c>.
    /// </summary>
    public static PixelPoint PlaceNearAnchor(PixelSize window, PixelRect anchor, PixelRect workArea, int gap = 10, int margin = 12)
    {
        var x = anchor.CenterX - window.Width / 2;
        var y = anchor.Top - gap - window.Height;
        if (y < workArea.Top + margin)
        {
            y = anchor.Bottom + gap; // doesn't fit above — show below instead
        }
        return Clamp(new PixelPoint(x, y), window, workArea, margin);
    }

    /// <summary>
    /// Adjacent to the taskbar, centered on the tray icon when its rect is known, else the corner
    /// near the system clock. Clamped into the work area.
    /// </summary>
    public static PixelPoint PlaceAtTray(PixelSize window, PixelRect? trayIconRect, PixelRect workArea, TaskbarEdge edge, int gap = 10, int margin = 12)
    {
        PixelPoint raw;
        if (trayIconRect is { } icon)
        {
            raw = edge switch
            {
                TaskbarEdge.Top => new PixelPoint(icon.CenterX - window.Width / 2, icon.Bottom + gap),
                TaskbarEdge.Left => new PixelPoint(icon.Right + gap, icon.CenterY - window.Height / 2),
                TaskbarEdge.Right => new PixelPoint(icon.Left - gap - window.Width, icon.CenterY - window.Height / 2),
                _ => new PixelPoint(icon.CenterX - window.Width / 2, icon.Top - gap - window.Height), // Bottom
            };
        }
        else
        {
            // No icon rect: fall back to the corner where the clock lives.
            raw = edge switch
            {
                TaskbarEdge.Top => new PixelPoint(workArea.Right - window.Width - margin, workArea.Top + margin),
                _ => new PixelPoint(workArea.Right - window.Width - margin, workArea.Bottom - window.Height - margin),
            };
        }
        return Clamp(raw, window, workArea, margin);
    }

    public static void Move(IntPtr hwnd, PixelPoint position) =>
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, position.X, position.Y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

    public static PixelRect GetWindowRect(IntPtr hwnd)
    {
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect.ToPixelRect();
    }

    /// <summary>WS_EX_TOOLWINDOW, not WS_EX_APPWINDOW — no taskbar button, no Alt+Tab entry.</summary>
    public static void SetToolWindow(IntPtr hwnd)
    {
        var exStyle = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle = (exStyle | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private static PixelPoint Clamp(PixelPoint point, PixelSize window, PixelRect workArea, int margin)
    {
        var x = ClampAxis(point.X, workArea.Left + margin, workArea.Right - window.Width - margin);
        var y = ClampAxis(point.Y, workArea.Top + margin, workArea.Bottom - window.Height - margin);
        return new PixelPoint(x, y);
    }

    private static int ClampAxis(int value, int min, int max) =>
        max < min ? min : Math.Max(min, Math.Min(value, max));
}
