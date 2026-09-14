using Translator.Core;
using Translator.Platform;
using Translator.UI.Windows;

namespace Translator.Tests.Platform;

/// <summary>
/// Mixed-DPI layouts that the development PC can't produce: a 100% primary monitor with a 150% monitor
/// below it (same geometry as the real two-monitor setup, different scale).
/// </summary>
public class MultiMonitorPlacementTests
{
    private static readonly MonitorMetrics Primary = new(
        new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1.0);

    // 1920x1200 below the primary, taskbar 48 DIPs = 72 px at 150%.
    private static readonly MonitorMetrics Secondary = new(
        new PixelRect(304, 1440, 2224, 2640), new PixelRect(304, 1440, 2224, 2568), 1.5);

    private static PixelSize PanelOn(MonitorMetrics monitor) =>
        new(monitor.ToPixels(PanelSizeMode.Standard.PanelWidth()), monitor.ToPixels(467));

    private static void AssertInside(PixelPoint position, PixelSize size, PixelRect area)
    {
        Assert.InRange(position.X, area.Left, area.Right - size.Width);
        Assert.InRange(position.Y, area.Top, area.Bottom - size.Height);
    }

    [Theory]
    [InlineData(400, 1.0, 400)]
    [InlineData(400, 1.5, 600)]
    [InlineData(330, 1.25, 413)]
    [InlineData(12, 1.75, 21)]
    [InlineData(10, 2.0, 20)]
    public void ToPixels_scales_and_rounds_half_away_from_zero(double dips, double scale, int pixels)
    {
        var monitor = Primary with { Scale = scale };

        Assert.Equal(pixels, monitor.ToPixels(dips));
    }

    [Fact]
    public void ToDips_inverts_ToPixels()
    {
        Assert.Equal(400, Secondary.ToDips(Secondary.ToPixels(400)));
    }

    [Fact]
    public void Panel_at_the_tray_corner_of_a_150_percent_monitor_uses_scaled_margins()
    {
        var size = PanelOn(Secondary);

        var position = WindowPlacement.PlaceAtTray(size, null, Secondary, TaskbarEdge.Bottom);

        Assert.Equal(new PixelSize(600, 701), size);
        Assert.Equal(Secondary.WorkArea.Right - size.Width - 18, position.X);
        Assert.Equal(Secondary.WorkArea.Bottom - size.Height - 18, position.Y);
        AssertInside(position, size, Secondary.WorkArea);
    }

    [Fact]
    public void Panel_reanchored_after_moving_to_a_higher_DPI_monitor_stays_in_its_work_area()
    {
        // First pass: size still measured at 100% on the primary; WM_DPICHANGED then grows the window.
        var before = PanelOn(Primary);
        var firstPosition = WindowPlacement.PlaceAtTray(before, null, Secondary, TaskbarEdge.Bottom);
        var after = PanelOn(Secondary);
        var finalPosition = WindowPlacement.PlaceAtTray(after, null, Secondary, TaskbarEdge.Bottom);

        AssertInside(firstPosition, before, Secondary.WorkArea);
        AssertInside(finalPosition, after, Secondary.WorkArea);
        // Anchored to the same corner: right and bottom margins don't change with the size.
        Assert.Equal(firstPosition.X + before.Width, finalPosition.X + after.Width);
        Assert.Equal(firstPosition.Y + before.Height, finalPosition.Y + after.Height);
    }

    [Fact]
    public void Bubble_near_the_top_of_the_lower_monitor_flips_below_instead_of_spilling_onto_the_primary()
    {
        var bubble = new PixelSize(Secondary.ToPixels(330), Secondary.ToPixels(120));
        var anchor = PixelRect.FromLtwh(1000, 1470, 3, 27);

        var position = WindowPlacement.PlaceNearAnchor(bubble, anchor, Secondary);

        Assert.Equal(anchor.Bottom + 15, position.Y);
        AssertInside(position, bubble, Secondary.WorkArea);
    }

    [Fact]
    public void Bubble_at_the_bottom_of_the_primary_stays_on_the_primary_with_unscaled_gap()
    {
        var bubble = new PixelSize(330, 120);
        var anchor = PixelRect.FromLtwh(1000, 1360, 2, 18);

        var position = WindowPlacement.PlaceNearAnchor(bubble, anchor, Primary);

        Assert.Equal(anchor.Top - 10 - bubble.Height, position.Y);
        AssertInside(position, bubble, Primary.WorkArea);
    }

    [Fact]
    public void Bubble_taller_than_a_small_scaled_work_area_pins_to_its_top_margin()
    {
        var small = new MonitorMetrics(new PixelRect(0, 1440, 1280, 2160), new PixelRect(0, 1440, 1280, 2100), 1.25);
        var bubble = new PixelSize(small.ToPixels(330), 800);

        var position = WindowPlacement.PlaceNearAnchor(bubble, PixelRect.FromLtwh(600, 1500, 2, 20), small);

        Assert.Equal(small.WorkArea.Top + 15, position.Y);
    }

    [Fact]
    public void Tray_reference_prefers_the_icon()
    {
        var icon = PixelRect.FromLtwh(2345, 1392, 32, 48);
        var taskbar = new PixelRect(0, 1392, 2560, 1440);
        var cursor = PixelRect.FromLtwh(1256, 2008, 16, 16);

        Assert.Equal(icon, WindowPlacement.TrayReference(icon, taskbar, cursor));
    }

    [Fact]
    public void Tray_reference_without_an_icon_rect_is_the_primary_taskbar_not_the_cursor_monitor()
    {
        var taskbar = new PixelRect(0, 1392, 2560, 1440);
        var cursorOnSecondary = PixelRect.FromLtwh(1256, 2008, 16, 16);

        Assert.Equal(taskbar, WindowPlacement.TrayReference(null, taskbar, cursorOnSecondary));
        Assert.Equal(cursorOnSecondary, WindowPlacement.TrayReference(null, null, cursorOnSecondary));
    }

    [Fact]
    public void Panel_at_a_primary_tray_icon_uses_primary_margins_even_with_a_150_percent_secondary()
    {
        var icon = PixelRect.FromLtwh(2345, 1392, 32, 48);
        var size = PanelOn(Primary);

        var position = WindowPlacement.PlaceAtTray(size, icon, Primary, TaskbarEdge.Bottom);

        // The icon's top is the work area's bottom, so the 12 px margin wins over the 10 px gap.
        Assert.Equal(Primary.WorkArea.Bottom - size.Height - 12, position.Y);
        Assert.Equal(Primary.WorkArea.Right - size.Width - 12, position.X); // centered on the icon, clamped at the edge
        AssertInside(position, size, Primary.WorkArea);
    }

    [Fact]
    public void Dragged_window_straddling_both_monitors_is_clamped_into_the_monitor_it_lands_on()
    {
        var size = PanelOn(Secondary);
        var straddling = PixelRect.FromLtwh(1800, 1300, size.Width, size.Height);

        var position = FloatingWindow.ClampInto(straddling, Secondary.WorkArea);

        AssertInside(position, size, Secondary.WorkArea);
        Assert.Equal(Secondary.WorkArea.Top, position.Y);
    }
}
