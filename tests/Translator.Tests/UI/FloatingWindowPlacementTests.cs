using Translator.Platform;
using Translator.UI.Windows;

namespace Translator.Tests.UI;

public class FloatingWindowPlacementTests
{
    private static readonly PixelRect WorkArea = PixelRect.FromLtwh(0, 0, 1920, 1040);

    [Fact]
    public void Window_inside_the_work_area_keeps_its_position()
    {
        var window = PixelRect.FromLtwh(100, 200, 400, 500);

        Assert.Equal(new PixelPoint(100, 200), FloatingWindow.ClampInto(window, WorkArea));
    }

    [Fact]
    public void Window_that_grew_below_the_taskbar_moves_up()
    {
        var window = PixelRect.FromLtwh(1500, 800, 400, 500);

        Assert.Equal(new PixelPoint(1500, 540), FloatingWindow.ClampInto(window, WorkArea));
    }

    [Fact]
    public void Window_dragged_past_the_left_edge_is_pulled_back()
    {
        var window = PixelRect.FromLtwh(-50, 10, 400, 500);

        Assert.Equal(new PixelPoint(0, 10), FloatingWindow.ClampInto(window, WorkArea));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DPI_change_while_dragging_across_monitors_never_snaps_back_to_the_anchor(bool movedBefore)
    {
        Assert.Equal(FloatingWindow.PositionSource.Keep, FloatingWindow.ChoosePositionSource(dragging: true, userMoved: movedBefore));
    }

    [Fact]
    public void After_a_drag_growth_keeps_the_user_position()
    {
        Assert.Equal(FloatingWindow.PositionSource.ClampUserPosition, FloatingWindow.ChoosePositionSource(dragging: false, userMoved: true));
    }

    [Fact]
    public void Untouched_window_follows_its_anchor()
    {
        Assert.Equal(FloatingWindow.PositionSource.Anchor, FloatingWindow.ChoosePositionSource(dragging: false, userMoved: false));
    }

    [Fact]
    public void Window_larger_than_the_work_area_pins_to_the_top_left()
    {
        var window = PixelRect.FromLtwh(300, 300, 3000, 2000);

        Assert.Equal(new PixelPoint(0, 0), FloatingWindow.ClampInto(window, WorkArea));
    }
}
