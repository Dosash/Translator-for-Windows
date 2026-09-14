namespace Translator.Platform.Capture;

/// <summary>
/// A 32-bpp BGRA pixel buffer (top-down, stride = width × 4, alpha 255) living only in memory.
/// <see cref="Clear"/> zeroes it once the pixels are no longer needed, so screen contents don't linger.
/// </summary>
public sealed class BgraImage
{
    public BgraImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != (long)width * height * 4)
        {
            throw new ArgumentException("Pixel buffer size doesn't match the dimensions.", nameof(pixels));
        }
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public void Clear() => Array.Clear(Pixels);

    /// <summary>Copies <paramref name="rect"/> (clamped to the image); null when nothing is left.</summary>
    public BgraImage? Crop(PixelRect rect)
    {
        var left = Math.Clamp(rect.Left, 0, Width);
        var top = Math.Clamp(rect.Top, 0, Height);
        var right = Math.Clamp(rect.Right, 0, Width);
        var bottom = Math.Clamp(rect.Bottom, 0, Height);
        int width = right - left, height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }
        var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(Pixels, ((top + y) * Width + left) * 4, result, y * width * 4, width * 4);
        }
        return new BgraImage(width, height, result);
    }

    /// <summary>
    /// Adds a border in the most common edge color (≈ the background): OCR misses glyphs that touch the image
    /// edge, which happens whenever the selection is drawn tightly around the text.
    /// </summary>
    public BgraImage Pad(int padding)
    {
        if (padding <= 0)
        {
            return this;
        }
        var (b, g, r) = DominantEdgeColor();
        int width = Width + padding * 2, height = Height + padding * 2;
        var result = new byte[width * height * 4];
        for (var i = 0; i < result.Length; i += 4)
        {
            result[i] = b;
            result[i + 1] = g;
            result[i + 2] = r;
            result[i + 3] = 255;
        }
        for (var y = 0; y < Height; y++)
        {
            Buffer.BlockCopy(Pixels, y * Width * 4, result, ((y + padding) * width + padding) * 4, Width * 4);
        }
        return new BgraImage(width, height, result);
    }

    /// <summary>Resamples by <paramref name="factor"/> with a separable Catmull-Rom (bicubic) filter.</summary>
    public BgraImage Scale(double factor)
    {
        var width = Math.Max(1, (int)Math.Round(Width * factor));
        var height = Math.Max(1, (int)Math.Round(Height * factor));
        if (width == Width && height == Height)
        {
            return this;
        }
        var horizontal = ResampleAxis(Pixels, Width, Height, width, horizontal: true);
        var result = ResampleAxis(horizontal, width, Height, height, horizontal: false);
        return new BgraImage(width, height, result);
    }

    private (byte B, byte G, byte R) DominantEdgeColor()
    {
        var counts = new Dictionary<int, int>();
        var best = (Key: 0, Count: -1);
        void Count(int x, int y)
        {
            var i = (y * Width + x) * 4;
            // 4-bit buckets per channel smooth out antialiasing noise.
            var key = (Pixels[i] >> 4) | (Pixels[i + 1] >> 4 << 4) | (Pixels[i + 2] >> 4 << 8);
            var count = counts[key] = counts.GetValueOrDefault(key) + 1;
            if (count > best.Count)
            {
                best = (key, count);
            }
        }
        for (var x = 0; x < Width; x++)
        {
            Count(x, 0);
            Count(x, Height - 1);
        }
        for (var y = 1; y < Height - 1; y++)
        {
            Count(0, y);
            Count(Width - 1, y);
        }
        static byte Center(int bucket) => (byte)((bucket & 0xF) * 17);
        return (Center(best.Key), Center(best.Key >> 4), Center(best.Key >> 8));
    }

    private static byte[] ResampleAxis(byte[] source, int sourceWidth, int sourceHeight, int targetLength, bool horizontal)
    {
        var sourceLength = horizontal ? sourceWidth : sourceHeight;
        var otherLength = horizontal ? sourceHeight : sourceWidth;
        var targetWidth = horizontal ? targetLength : sourceWidth;
        var targetHeight = horizontal ? sourceHeight : targetLength;
        var result = new byte[targetWidth * targetHeight * 4];

        var ratio = (double)sourceLength / targetLength;
        // Widen the kernel when shrinking so downscaling averages instead of aliasing.
        var support = Math.Max(1.0, ratio);
        var radius = (int)Math.Ceiling(2 * support);
        var taps = new (int[] Index, double[] Weight)[targetLength];
        for (var t = 0; t < targetLength; t++)
        {
            var center = (t + 0.5) * ratio - 0.5;
            var first = (int)Math.Floor(center) - radius + 1;
            var index = new int[radius * 2];
            var weight = new double[radius * 2];
            double sum = 0;
            for (var k = 0; k < index.Length; k++)
            {
                var s = first + k;
                index[k] = Math.Clamp(s, 0, sourceLength - 1);
                weight[k] = CatmullRom((s - center) / support);
                sum += weight[k];
            }
            for (var k = 0; k < weight.Length; k++)
            {
                weight[k] /= sum;
            }
            taps[t] = (index, weight);
        }

        for (var o = 0; o < otherLength; o++)
        {
            for (var t = 0; t < targetLength; t++)
            {
                var (index, weight) = taps[t];
                double b = 0, g = 0, r = 0;
                for (var k = 0; k < index.Length; k++)
                {
                    var i = horizontal ? (o * sourceWidth + index[k]) * 4 : (index[k] * sourceWidth + o) * 4;
                    b += source[i] * weight[k];
                    g += source[i + 1] * weight[k];
                    r += source[i + 2] * weight[k];
                }
                var j = horizontal ? (o * targetWidth + t) * 4 : (t * targetWidth + o) * 4;
                result[j] = ToByte(b);
                result[j + 1] = ToByte(g);
                result[j + 2] = ToByte(r);
                result[j + 3] = 255;
            }
        }
        return result;
    }

    private static double CatmullRom(double x)
    {
        x = Math.Abs(x);
        return x switch
        {
            < 1 => 1.5 * x * x * x - 2.5 * x * x + 1,
            < 2 => -0.5 * x * x * x + 2.5 * x * x - 4 * x + 2,
            _ => 0,
        };
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
