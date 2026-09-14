using System.Windows;
using Translator.Platform;

namespace Translator.UI.ScreenCapture;

/// <summary>
/// Pure math for the screen-area overlay (unit-tested): the drag rectangle in screen pixels, clamped to the
/// monitor the drag started on, and the mapping between screen pixels and the overlay's DIPs.
/// </summary>
public static class SelectionGeometry
{
    /// <summary>Drags smaller than this (on either side) count as a click and cancel.</summary>
    public const double MinSelectionDips = 8;

    /// <summary>
    /// The rectangle spanned by <paramref name="start"/> and <paramref name="current"/> (screen pixels, right and
    /// bottom exclusive), clamped to <paramref name="monitor"/> so a drag that wanders onto another monitor stays put.
    /// </summary>
    public static PixelRect Normalize(PixelPoint start, PixelPoint current, PixelRect monitor)
    {
        var x1 = Clamp(start.X, monitor.Left, monitor.Right);
        var y1 = Clamp(start.Y, monitor.Top, monitor.Bottom);
        var x2 = Clamp(current.X, monitor.Left, monitor.Right);
        var y2 = Clamp(current.Y, monitor.Top, monitor.Bottom);
        return new PixelRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    /// <summary>True for a click or a sliver that can't hold readable text.</summary>
    public static bool IsTooSmall(PixelRect selection, double scale)
    {
        var min = (int)Math.Round(MinSelectionDips * scale, MidpointRounding.AwayFromZero);
        return selection.Width < min || selection.Height < min;
    }

    /// <summary>A point inside the overlay (DIPs from its top-left) to screen pixels.</summary>
    public static PixelPoint ToScreenPixels(Point overlayDips, PixelRect monitor, double scale) =>
        new(monitor.Left + (int)Math.Round(overlayDips.X * scale, MidpointRounding.AwayFromZero),
            monitor.Top + (int)Math.Round(overlayDips.Y * scale, MidpointRounding.AwayFromZero));

    /// <summary>A screen-pixel rectangle to the overlay's DIPs (the overlay covers <paramref name="monitor"/> exactly).</summary>
    public static Rect ToOverlayDips(PixelRect rect, PixelRect monitor, double scale) =>
        new((rect.Left - monitor.Left) / scale, (rect.Top - monitor.Top) / scale, rect.Width / scale, rect.Height / scale);

    /// <summary>The selection in the monitor snapshot's own pixel coordinates.</summary>
    public static PixelRect ToSnapshotPixels(PixelRect rect, PixelRect monitor) =>
        new(rect.Left - monitor.Left, rect.Top - monitor.Top, rect.Right - monitor.Left, rect.Bottom - monitor.Top);

    /// <summary>
    /// Top-left of the "W × H" label (DIPs): below the selection's bottom-left corner, above it when there's no
    /// room below, and kept inside the overlay.
    /// </summary>
    public static Point PlaceSizeLabel(Rect selectionDips, Size label, Size overlay, double gap = 6)
    {
        var x = Math.Max(0, Math.Min(selectionDips.Left, overlay.Width - label.Width));
        var y = selectionDips.Bottom + gap;
        if (y + label.Height > overlay.Height)
        {
            y = selectionDips.Top - gap - label.Height;
        }
        if (y < 0)
        {
            y = Math.Max(0, selectionDips.Bottom - gap - label.Height);
        }
        return new Point(x, y);
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));
}
