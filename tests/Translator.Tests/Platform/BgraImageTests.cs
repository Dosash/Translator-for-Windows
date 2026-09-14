using Translator.Platform;
using Translator.Platform.Capture;

namespace Translator.Tests.Platform;

public class BgraImageTests
{
    private static BgraImage Filled(int width, int height, byte b, byte g, byte r)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }
        return new BgraImage(width, height, pixels);
    }

    private static (byte B, byte G, byte R) PixelAt(BgraImage image, int x, int y)
    {
        var i = (y * image.Width + x) * 4;
        return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2]);
    }

    private static void SetPixel(BgraImage image, int x, int y, byte value)
    {
        var i = (y * image.Width + x) * 4;
        image.Pixels[i] = image.Pixels[i + 1] = image.Pixels[i + 2] = value;
    }

    [Fact]
    public void Crop_copies_the_rectangle()
    {
        var image = Filled(10, 8, 255, 255, 255);
        SetPixel(image, 3, 2, 0);

        var crop = image.Crop(new PixelRect(3, 2, 7, 5));

        Assert.NotNull(crop);
        Assert.Equal((4, 3), (crop.Width, crop.Height));
        Assert.Equal(((byte)0, (byte)0, (byte)0), PixelAt(crop, 0, 0));
        Assert.Equal(((byte)255, (byte)255, (byte)255), PixelAt(crop, 1, 0));
    }

    [Fact]
    public void Crop_is_clamped_and_empty_crops_are_null()
    {
        var image = Filled(10, 8, 1, 2, 3);

        Assert.Equal((4, 3), (image.Crop(new PixelRect(6, 5, 20, 20))!.Width, image.Crop(new PixelRect(6, 5, 20, 20))!.Height));
        Assert.Null(image.Crop(new PixelRect(12, 0, 20, 5)));
    }

    [Fact]
    public void Pad_uses_the_dominant_edge_color()
    {
        var image = Filled(20, 10, 250, 250, 250);
        for (var x = 0; x < 6; x++)
        {
            SetPixel(image, x, 0, 10); // text touching the top edge
        }

        var padded = image.Pad(4);

        Assert.Equal((28, 18), (padded.Width, padded.Height));
        Assert.Equal(((byte)255, (byte)255, (byte)255), PixelAt(padded, 0, 0));
        Assert.Equal(((byte)10, (byte)10, (byte)10), PixelAt(padded, 4, 4));
        Assert.Equal(255, padded.Pixels[3]);
    }

    [Fact]
    public void Scale_up_three_times_keeps_flat_colors_and_opaque_alpha()
    {
        var scaled = Filled(7, 5, 40, 120, 200).Scale(3);

        Assert.Equal((21, 15), (scaled.Width, scaled.Height));
        Assert.Equal(((byte)40, (byte)120, (byte)200), PixelAt(scaled, 10, 7));
        Assert.All(Enumerable.Range(0, scaled.Width * scaled.Height), i => Assert.Equal(255, scaled.Pixels[i * 4 + 3]));
    }

    [Fact]
    public void Scale_down_and_identity()
    {
        var image = Filled(40, 20, 9, 9, 9);

        Assert.Equal((20, 10), (image.Scale(0.5).Width, image.Scale(0.5).Height));
        Assert.Same(image, image.Scale(1));
    }

    [Fact]
    public void Upscaled_edge_stays_sharp_enough_to_read()
    {
        var image = Filled(4, 1, 255, 255, 255);
        SetPixel(image, 2, 0, 0);
        SetPixel(image, 3, 0, 0);

        var scaled = image.Scale(3);

        Assert.True(PixelAt(scaled, 1, 0).B > 200);
        Assert.True(PixelAt(scaled, 10, 0).B < 50);
    }

    [Fact]
    public void Clear_zeroes_the_pixels()
    {
        var image = Filled(3, 3, 1, 2, 3);

        image.Clear();

        Assert.All(image.Pixels, value => Assert.Equal(0, value));
    }
}
