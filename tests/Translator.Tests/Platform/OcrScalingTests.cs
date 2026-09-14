using Translator.Platform.Ocr;

namespace Translator.Tests.Platform;

public class OcrScalingTests
{
    private const int Win11Limit = 10000;

    [Theory]
    [InlineData(300, 40, 3)]
    [InlineData(1200, 59, 3)]
    [InlineData(1200, 60, 1)]
    [InlineData(800, 400, 1)]
    public void Short_regions_are_upscaled_three_times_on_the_first_pass(int width, int height, double expected)
    {
        Assert.Equal(expected, OcrScaling.InitialScale(width, height, Win11Limit, padding: 16));
    }

    [Fact]
    public void Upscale_is_reduced_to_a_whole_factor_that_fits_the_engine_limit()
    {
        // (10000 - 32) / 4000 = 2.49 → ×2
        Assert.Equal(2, OcrScaling.InitialScale(4000, 50, Win11Limit, padding: 16));
        // A 2600 px limit (older Windows 10) leaves no room to upscale a 2000 px wide strip.
        Assert.Equal(1, OcrScaling.InitialScale(2000, 50, 2600, padding: 16));
    }

    [Fact]
    public void Regions_larger_than_the_engine_limit_are_shrunk_to_fit()
    {
        var scale = OcrScaling.InitialScale(12000, 3000, Win11Limit, padding: 16);

        Assert.True(scale < 1);
        Assert.True(12000 * scale + 32 <= Win11Limit);
    }

    [Fact]
    public void Nothing_found_at_one_x_retries_at_two_x()
    {
        Assert.Equal(2, OcrScaling.RetryScale(800, 300, Win11Limit, usedScale: 1, foundText: false, medianLineHeight: null, padding: 16));
    }

    [Theory]
    [InlineData(12, 3)]
    [InlineData(16, 3)]
    [InlineData(18, 2)]
    [InlineData(19.9, 2)]
    public void Small_recognized_lines_retry_upscaled_toward_a_comfortable_height(double lineHeight, double expected)
    {
        Assert.Equal(expected, OcrScaling.RetryScale(800, 300, Win11Limit, 1, foundText: true, lineHeight, padding: 16));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(32)]
    public void Readable_lines_are_not_retried(double lineHeight)
    {
        Assert.Null(OcrScaling.RetryScale(800, 300, Win11Limit, 1, foundText: true, lineHeight, padding: 16));
    }

    [Fact]
    public void A_pass_that_was_already_upscaled_is_not_retried()
    {
        Assert.Null(OcrScaling.RetryScale(300, 40, Win11Limit, usedScale: 3, foundText: false, medianLineHeight: null, padding: 16));
    }

    [Fact]
    public void No_retry_when_the_engine_limit_leaves_no_room_to_upscale()
    {
        Assert.Null(OcrScaling.RetryScale(2000, 300, 2600, 1, foundText: false, medianLineHeight: null, padding: 16));
    }
}
