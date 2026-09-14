namespace Translator.Platform.Ocr;

/// <summary>
/// How much to resample the selected region before OCR (pure, unit-tested). Windows OCR reads text best at
/// roughly 20–40 px cap height; typical 100% UI text is 9–12 px, so small text is upscaled.
/// </summary>
public static class OcrScaling
{
    /// <summary>Below this region height the text is certainly small: upscale on the first pass.</summary>
    public const int SmallRegionHeight = 60;

    /// <summary>Recognized lines lower than this (in source pixels) are worth a second, upscaled pass.</summary>
    public const double SmallLineHeight = 20;

    public const int MaxUpscale = 3;

    private const double TargetLineHeight = 36;

    /// <summary>First pass: ×3 for short regions, otherwise as-is — shrunk only when above the engine's limit.</summary>
    public static double InitialScale(int width, int height, int maxImageDimension, int padding = 0)
    {
        var preferred = height < SmallRegionHeight ? MaxUpscale : 1;
        return Fit(preferred, width, height, maxImageDimension, padding);
    }

    /// <summary>
    /// Second pass after a first one at <paramref name="usedScale"/>: ×2 when nothing was found, or enough to lift
    /// the median line to a comfortable height. Null when a retry wouldn't be any bigger.
    /// </summary>
    public static double? RetryScale(int width, int height, int maxImageDimension, double usedScale, bool foundText, double? medianLineHeight, int padding = 0)
    {
        if (usedScale > 1)
        {
            return null;
        }
        double preferred;
        if (!foundText)
        {
            preferred = 2;
        }
        else if (medianLineHeight is { } lineHeight && lineHeight > 0 && lineHeight < SmallLineHeight)
        {
            preferred = Math.Clamp(Math.Ceiling(TargetLineHeight / lineHeight), 2, MaxUpscale);
        }
        else
        {
            return null;
        }
        var scale = Fit(preferred, width, height, maxImageDimension, padding);
        return scale >= 1.5 ? scale : null;
    }

    /// <summary>The largest scale ≤ <paramref name="preferred"/> that keeps the padded image within the engine's limit.</summary>
    internal static double Fit(double preferred, int width, int height, int maxImageDimension, int padding)
    {
        var longest = Math.Max(width, height);
        if (longest <= 0 || maxImageDimension <= 0)
        {
            return 1;
        }
        var limit = (double)(maxImageDimension - padding * 2) / longest;
        if (limit >= preferred)
        {
            return preferred;
        }
        // Whole factors keep strokes crisp; below 1 any factor that fits will do.
        return limit >= 1 ? Math.Floor(limit) : limit;
    }
}
