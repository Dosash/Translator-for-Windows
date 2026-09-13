using System.Windows;
using System.Windows.Media;

namespace Translator.Platform;

/// <summary>
/// The app's own mark: a speech bubble with a "⇄" cut out of it. No borrowed glyphs or images —
/// ported from macOS <c>MenuBarIcon.swift</c>, which draws in an 18×18, y-up box; this flips y for
/// WPF's y-down box. Reused by the tray icon (rendered to an HICON) and the UI's logo.
/// </summary>
public static class TrayGlyph
{
    private const double Side = 18.0;

    public static Geometry CreateGeometry()
    {
        // Bubble: mac rect (x:1,y:4,w:16,h:12) with bottom-left origin -> WPF top-left origin.
        var bubble = new RectangleGeometry(new Rect(1, Flip(4 + 12), 16, 12), 3.6, 3.6);

        var tail = new StreamGeometry();
        using (var ctx = tail.Open())
        {
            ctx.BeginFigure(new Point(4.2, Flip(5.5)), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(2.6, Flip(1.2)), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(7.8, Flip(4.4)), isStroked: false, isSmoothJoin: false);
        }

        var bubbleAndTail = new GeometryGroup { FillRule = FillRule.Nonzero };
        bubbleAndTail.Children.Add(bubble);
        bubbleAndTail.Children.Add(tail);

        var arrows = new StreamGeometry();
        using (var ctx = arrows.Open())
        {
            // Top arrow "→": bar + arrowhead.
            ctx.BeginFigure(new Point(4.2, Flip(10.9 + 1.5)), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(4.2 + 6.4, Flip(10.9 + 1.5)), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(4.2 + 6.4, Flip(10.9)), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(4.2, Flip(10.9)), isStroked: false, isSmoothJoin: false);

            ctx.BeginFigure(new Point(10.4, Flip(9.7)), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(13.9, Flip(11.65)), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(10.4, Flip(13.6)), isStroked: false, isSmoothJoin: false);

            // Bottom arrow "←": bar + arrowhead.
            ctx.BeginFigure(new Point(7.4, Flip(6.6 + 1.5)), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(7.4 + 6.4, Flip(6.6 + 1.5)), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(7.4 + 6.4, Flip(6.6)), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(7.4, Flip(6.6)), isStroked: false, isSmoothJoin: false);

            ctx.BeginFigure(new Point(7.6, Flip(5.05)), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(4.1, Flip(7.35)), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(7.6, Flip(9.3)), isStroked: false, isSmoothJoin: false);
        }

        var result = new CombinedGeometry(GeometryCombineMode.Exclude, bubbleAndTail, arrows);
        result.Freeze();
        return result;
    }

    /// <summary>Mac coordinates are y-up from the bottom of the 18×18 box; WPF is y-down from the top.</summary>
    private static double Flip(double macY) => Side - macY;
}
