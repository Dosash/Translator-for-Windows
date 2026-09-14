namespace Translator.Platform;

/// <summary>A monitor in physical pixels with its effective scale (1.0 = 96 DPI, 1.5 = 150%).</summary>
public readonly record struct MonitorMetrics(PixelRect Bounds, PixelRect WorkArea, double Scale)
{
    /// <summary>Device-independent pixels to physical pixels on this monitor.</summary>
    public int ToPixels(double dips) => (int)Math.Round(dips * Scale, MidpointRounding.AwayFromZero);

    public double ToDips(int pixels) => pixels / Scale;
}
