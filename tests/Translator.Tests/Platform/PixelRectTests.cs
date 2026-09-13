using Translator.Platform;

namespace Translator.Tests.Platform;

public class PixelRectTests
{
    [Fact]
    public void FromLtwh_computes_right_and_bottom()
    {
        var rect = PixelRect.FromLtwh(10, 20, 100, 50);
        Assert.Equal(10, rect.Left);
        Assert.Equal(20, rect.Top);
        Assert.Equal(110, rect.Right);
        Assert.Equal(70, rect.Bottom);
        Assert.Equal(100, rect.Width);
        Assert.Equal(50, rect.Height);
    }

    [Fact]
    public void Center_is_the_midpoint()
    {
        var rect = PixelRect.FromLtwh(0, 0, 100, 40);
        Assert.Equal(50, rect.CenterX);
        Assert.Equal(20, rect.CenterY);
    }

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(0, 0, true)]
    [InlineData(10, 5, false)] // right edge excluded
    [InlineData(-1, 5, false)]
    public void Contains_uses_half_open_bounds(int x, int y, bool expected)
    {
        var rect = PixelRect.FromLtwh(0, 0, 10, 10);
        Assert.Equal(expected, rect.Contains(new PixelPoint(x, y)));
    }

    [Fact]
    public void Intersects_detects_overlap()
    {
        var a = PixelRect.FromLtwh(0, 0, 10, 10);
        var b = PixelRect.FromLtwh(5, 5, 10, 10);
        var c = PixelRect.FromLtwh(20, 20, 5, 5);
        Assert.True(a.Intersects(b));
        Assert.False(a.Intersects(c));
    }

    [Fact]
    public void Inflate_grows_on_every_side()
    {
        var rect = PixelRect.FromLtwh(10, 10, 10, 10).Inflate(2, 3);
        Assert.Equal(new PixelRect(8, 7, 22, 23), rect);
    }
}
