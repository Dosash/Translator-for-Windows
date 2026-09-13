using Translator.Platform;

namespace Translator.Tests.Platform;

public class TrayGlyphTests
{
    [Fact]
    public void Geometry_is_frozen_and_stays_within_the_18x18_box()
    {
        var geometry = TrayGlyph.CreateGeometry();

        Assert.True(geometry.IsFrozen);
        var bounds = geometry.Bounds;
        Assert.InRange(bounds.Left, 0, 18);
        Assert.InRange(bounds.Top, 0, 18);
        Assert.InRange(bounds.Right, 0, 18);
        Assert.InRange(bounds.Bottom, 0, 18);
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
    }
}
