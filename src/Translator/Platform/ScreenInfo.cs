using Translator.Core;

namespace Translator.Platform;

public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>Monitor geometry, DPI, cursor position and taskbar placement — all in physical pixels.</summary>
public static class ScreenInfo
{
    public static PixelRect GetWorkArea(PixelRect near) => GetMonitorInfo(near).rcWork.ToPixelRect();

    public static PixelRect GetMonitorBounds(PixelRect near) => GetMonitorInfo(near).rcMonitor.ToPixelRect();

    /// <summary>1.0 at 96 DPI (100% scaling).</summary>
    public static double GetScale(PixelRect near)
    {
        var monitor = MonitorFromRect(near);
        if (monitor == IntPtr.Zero)
        {
            return 1.0;
        }
        var hr = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _);
        if (hr != 0 || dpiX == 0)
        {
            DebugLog.Write($"ScreenInfo.GetScale: GetDpiForMonitor failed, hr={hr}");
            return 1.0;
        }
        return dpiX / 96.0;
    }

    public static PixelPoint GetCursorPosition()
    {
        NativeMethods.GetCursorPos(out var point);
        return new PixelPoint(point.X, point.Y);
    }

    /// <summary>Which side of the monitor the taskbar occupies, comparing the monitor's full bounds to its work area.</summary>
    public static TaskbarEdge GetTaskbarEdge(PixelRect monitorBounds, PixelRect workArea)
    {
        if (workArea.Top > monitorBounds.Top) return TaskbarEdge.Top;
        if (workArea.Bottom < monitorBounds.Bottom) return TaskbarEdge.Bottom;
        if (workArea.Left > monitorBounds.Left) return TaskbarEdge.Left;
        if (workArea.Right < monitorBounds.Right) return TaskbarEdge.Right;

        // Work area == monitor bounds: an auto-hidden taskbar reserves no space. Ask explicitly.
        var data = new NativeMethods.APPBARDATA { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>() };
        if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref data) != IntPtr.Zero)
        {
            return data.uEdge switch
            {
                0 => TaskbarEdge.Left,
                1 => TaskbarEdge.Top,
                2 => TaskbarEdge.Right,
                _ => TaskbarEdge.Bottom,
            };
        }
        return TaskbarEdge.Bottom;
    }

    private static NativeMethods.MONITORINFO GetMonitorInfo(PixelRect near)
    {
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        var monitor = MonitorFromRect(near);
        if (monitor != IntPtr.Zero)
        {
            NativeMethods.GetMonitorInfo(monitor, ref info);
        }
        return info;
    }

    private static IntPtr MonitorFromRect(PixelRect rect)
    {
        var native = rect.ToRect();
        return NativeMethods.MonitorFromRect(in native, NativeMethods.MONITOR_DEFAULTTONEAREST);
    }
}

internal static class RectConversions
{
    public static NativeMethods.RECT ToRect(this PixelRect rect) => new()
    {
        Left = rect.Left,
        Top = rect.Top,
        Right = rect.Right,
        Bottom = rect.Bottom,
    };

    public static PixelRect ToPixelRect(this NativeMethods.RECT rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
