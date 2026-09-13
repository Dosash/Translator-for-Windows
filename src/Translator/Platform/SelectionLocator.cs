namespace Translator.Platform;

/// <summary>Where on screen the selected text is, so the translation bubble shows up next to it.</summary>
public static class SelectionLocator
{
    /// <summary>
    /// The caret rect of the foreground thread, converted to screen coordinates. Null when the
    /// foreground app has no caret (e.g. selection made with the mouse only, or an app that draws
    /// its own caret outside the standard caret API).
    /// </summary>
    public static PixelRect? GetCaretRect()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return null;
        }
        var threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var info = new NativeMethods.GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info) || info.hwndCaret == IntPtr.Zero)
        {
            return null;
        }

        var topLeft = new NativeMethods.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
        var bottomRight = new NativeMethods.POINT { X = info.rcCaret.Right, Y = info.rcCaret.Bottom };
        NativeMethods.ClientToScreen(info.hwndCaret, ref topLeft);
        NativeMethods.ClientToScreen(info.hwndCaret, ref bottomRight);
        return new PixelRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    /// <summary>Fallback: a small square around the mouse cursor — the user just selected text there.</summary>
    public static PixelRect GetMouseRect()
    {
        var cursor = ScreenInfo.GetCursorPosition();
        return PixelRect.FromLtwh(cursor.X - 8, cursor.Y - 8, 16, 16);
    }

    /// <summary>The caret rect when available, else the mouse rect.</summary>
    public static PixelRect GetAnchor() => GetCaretRect() ?? GetMouseRect();
}
