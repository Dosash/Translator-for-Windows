using System.Windows;
using Translator.Platform;
using Translator.UI.ScreenCapture;

namespace Translator.Tests.UI;

/// <summary>Same two-monitor layout as the development PC, with the lower monitor at 150% to cover mixed DPI.</summary>
public class SelectionGeometryTests
{
    private static readonly PixelRect Primary = new(0, 0, 2560, 1440);
    private static readonly PixelRect Secondary = new(304, 1440, 2224, 2640);

    [Fact]
    public void Drag_down_right_spans_start_to_current()
    {
        var rect = SelectionGeometry.Normalize(new PixelPoint(100, 120), new PixelPoint(420, 260), Primary);

        Assert.Equal(new PixelRect(100, 120, 420, 260), rect);
    }

    [Fact]
    public void Drag_up_left_is_normalized()
    {
        var rect = SelectionGeometry.Normalize(new PixelPoint(420, 260), new PixelPoint(100, 120), Primary);

        Assert.Equal(new PixelRect(100, 120, 420, 260), rect);
    }

    [Fact]
    public void Drag_that_wanders_onto_the_primary_monitor_stays_on_the_secondary()
    {
        var rect = SelectionGeometry.Normalize(new PixelPoint(1000, 2000), new PixelPoint(2500, 1300), Secondary);

        Assert.Equal(new PixelRect(1000, 1440, 2224, 2000), rect);
    }

    [Fact]
    public void Drag_beyond_the_outer_edges_is_clamped_to_the_monitor()
    {
        var rect = SelectionGeometry.Normalize(new PixelPoint(-50, -20), new PixelPoint(2700, 1500), Primary);

        Assert.Equal(Primary, rect);
    }

    [Theory]
    [InlineData(0, 0, 1.0, true)]
    [InlineData(7, 100, 1.0, true)]
    [InlineData(100, 7, 1.0, true)]
    [InlineData(8, 8, 1.0, false)]
    [InlineData(11, 40, 1.5, true)]
    [InlineData(12, 12, 1.5, false)]
    public void Tiny_selections_cancel_with_a_threshold_scaled_per_monitor(int width, int height, double scale, bool tooSmall)
    {
        Assert.Equal(tooSmall, SelectionGeometry.IsTooSmall(PixelRect.FromLtwh(500, 500, width, height), scale));
    }

    [Fact]
    public void Overlay_point_on_a_150_percent_monitor_maps_to_screen_pixels()
    {
        var pixel = SelectionGeometry.ToScreenPixels(new Point(100.5, 20), Secondary, 1.5);

        Assert.Equal(new PixelPoint(304 + 151, 1440 + 30), pixel);
    }

    [Fact]
    public void Overlay_point_on_a_100_percent_monitor_is_the_screen_pixel()
    {
        Assert.Equal(new PixelPoint(1234, 567), SelectionGeometry.ToScreenPixels(new Point(1234, 567), Primary, 1.0));
    }

    [Fact]
    public void Screen_rectangle_on_a_150_percent_monitor_maps_to_overlay_dips()
    {
        var dips = SelectionGeometry.ToOverlayDips(new PixelRect(454, 1590, 754, 1740), Secondary, 1.5);

        Assert.Equal(new Rect(100, 100, 200, 100), dips);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    public void Pixels_survive_a_round_trip_through_overlay_dips(double scale)
    {
        for (var x = Secondary.Left; x < Secondary.Left + 40; x++)
        {
            var rect = new PixelRect(x, 1500 + x % 7, x + 33, 1600);
            var dips = SelectionGeometry.ToOverlayDips(rect, Secondary, scale);

            Assert.Equal(new PixelPoint(rect.Left, rect.Top), SelectionGeometry.ToScreenPixels(dips.TopLeft, Secondary, scale));
            Assert.Equal(new PixelPoint(rect.Right, rect.Bottom), SelectionGeometry.ToScreenPixels(dips.BottomRight, Secondary, scale));
        }
    }

    [Fact]
    public void Snapshot_pixels_are_relative_to_the_monitor()
    {
        Assert.Equal(new PixelRect(150, 150, 450, 300), SelectionGeometry.ToSnapshotPixels(new PixelRect(454, 1590, 754, 1740), Secondary));
    }

    [Fact]
    public void Size_label_goes_below_the_selection()
    {
        var position = SelectionGeometry.PlaceSizeLabel(new Rect(100, 100, 200, 50), new Size(60, 20), new Size(1000, 800));

        Assert.Equal(new Point(100, 156), position);
    }

    [Fact]
    public void Size_label_flips_above_at_the_bottom_edge_and_stays_inside_horizontally()
    {
        var position = SelectionGeometry.PlaceSizeLabel(new Rect(970, 700, 30, 95), new Size(60, 20), new Size(1000, 800));

        Assert.Equal(new Point(940, 674), position);
    }

    [Fact]
    public void Size_label_for_a_full_height_selection_sits_inside_it()
    {
        var position = SelectionGeometry.PlaceSizeLabel(new Rect(0, 0, 1000, 800), new Size(60, 20), new Size(1000, 800));

        Assert.Equal(new Point(0, 774), position);
    }
}
