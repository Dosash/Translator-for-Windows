using Translator.Platform;

namespace Translator.Tests.Platform;

public class WindowPlacementTests
{
    private static readonly PixelRect WorkArea = PixelRect.FromLtwh(0, 0, 1920, 1040); // taskbar at the bottom
    private static readonly PixelSize Window = new(330, 200);

    [Fact]
    public void PlaceNearAnchor_prefers_above_when_it_fits()
    {
        var anchor = PixelRect.FromLtwh(900, 500, 20, 20);
        var point = WindowPlacement.PlaceNearAnchor(Window, anchor, WorkArea);

        Assert.Equal(anchor.CenterX - Window.Width / 2, point.X);
        Assert.Equal(anchor.Top - 10 - Window.Height, point.Y);
    }

    [Fact]
    public void PlaceNearAnchor_flips_below_when_above_does_not_fit()
    {
        var anchor = PixelRect.FromLtwh(900, 20, 20, 20); // too close to the top
        var point = WindowPlacement.PlaceNearAnchor(Window, anchor, WorkArea);

        Assert.Equal(anchor.Bottom + 10, point.Y);
    }

    [Fact]
    public void PlaceNearAnchor_clamps_into_the_work_area()
    {
        var anchor = PixelRect.FromLtwh(-100, 500, 20, 20); // off the left edge
        var point = WindowPlacement.PlaceNearAnchor(Window, anchor, WorkArea);

        Assert.Equal(WorkArea.Left + 12, point.X);
    }

    [Fact]
    public void PlaceNearAnchor_clamps_right_edge_too()
    {
        var anchor = PixelRect.FromLtwh(1900, 500, 20, 20); // off the right edge
        var point = WindowPlacement.PlaceNearAnchor(Window, anchor, WorkArea);

        Assert.Equal(WorkArea.Right - Window.Width - 12, point.X);
    }

    [Theory]
    [InlineData(TaskbarEdge.Bottom)]
    [InlineData(TaskbarEdge.Top)]
    [InlineData(TaskbarEdge.Left)]
    [InlineData(TaskbarEdge.Right)]
    public void PlaceAtTray_centers_on_the_icon_when_known(TaskbarEdge edge)
    {
        var icon = PixelRect.FromLtwh(1800, 1000, 16, 16);
        var point = WindowPlacement.PlaceAtTray(Window, icon, WorkArea, edge);

        // Always inside the work area with the margin respected.
        Assert.InRange(point.X, WorkArea.Left + 12, WorkArea.Right - Window.Width - 12);
        Assert.InRange(point.Y, WorkArea.Top + 12, WorkArea.Bottom - Window.Height - 12);
    }

    [Fact]
    public void PlaceAtTray_bottom_edge_sits_above_the_icon()
    {
        var icon = PixelRect.FromLtwh(1000, 1000, 16, 16);
        var point = WindowPlacement.PlaceAtTray(Window, icon, WorkArea, TaskbarEdge.Bottom);

        Assert.True(point.Y < icon.Top);
    }

    [Fact]
    public void PlaceAtTray_top_edge_sits_below_the_icon()
    {
        var icon = PixelRect.FromLtwh(1000, 10, 16, 16);
        var point = WindowPlacement.PlaceAtTray(Window, icon, WorkArea, TaskbarEdge.Top);

        Assert.True(point.Y > icon.Top);
    }

    [Fact]
    public void PlaceAtTray_left_edge_sits_right_of_the_icon()
    {
        var icon = PixelRect.FromLtwh(0, 500, 16, 16);
        var point = WindowPlacement.PlaceAtTray(Window, icon, WorkArea, TaskbarEdge.Left);

        Assert.True(point.X > icon.Left);
    }

    [Fact]
    public void PlaceAtTray_right_edge_sits_left_of_the_icon()
    {
        var icon = PixelRect.FromLtwh(1900, 500, 16, 16);
        var point = WindowPlacement.PlaceAtTray(Window, icon, WorkArea, TaskbarEdge.Right);

        Assert.True(point.X < icon.Left);
    }

    [Theory]
    [InlineData(TaskbarEdge.Bottom)]
    [InlineData(TaskbarEdge.Top)]
    [InlineData(TaskbarEdge.Left)]
    [InlineData(TaskbarEdge.Right)]
    public void PlaceAtTray_without_an_icon_rect_falls_back_to_a_corner_inside_the_work_area(TaskbarEdge edge)
    {
        var point = WindowPlacement.PlaceAtTray(Window, null, WorkArea, edge);

        Assert.InRange(point.X, WorkArea.Left + 12, WorkArea.Right - Window.Width - 12);
        Assert.InRange(point.Y, WorkArea.Top + 12, WorkArea.Bottom - Window.Height - 12);
    }

    [Fact]
    public void PlaceNearAnchor_does_not_crash_when_the_window_is_larger_than_the_work_area()
    {
        var tinyWorkArea = PixelRect.FromLtwh(0, 0, 100, 100);
        var anchor = PixelRect.FromLtwh(40, 40, 10, 10);
        var point = WindowPlacement.PlaceNearAnchor(new PixelSize(500, 500), anchor, tinyWorkArea);

        Assert.Equal(tinyWorkArea.Left + 12, point.X);
        Assert.Equal(tinyWorkArea.Top + 12, point.Y);
    }
}
