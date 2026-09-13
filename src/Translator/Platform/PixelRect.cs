namespace Translator.Platform;

/// <summary>A point in physical screen pixels.</summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>A size in physical screen pixels.</summary>
public readonly record struct PixelSize(int Width, int Height);

/// <summary>
/// A rectangle in physical screen pixels (not DIPs). All geometry crossing the Platform
/// boundary uses this type so mixed-DPI multi-monitor setups work with plain <c>SetWindowPos</c>.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public int CenterX => Left + Width / 2;

    public int CenterY => Top + Height / 2;

    public static PixelRect FromLtwh(int left, int top, int width, int height) =>
        new(left, top, left + width, top + height);

    public bool Contains(PixelPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public bool Intersects(PixelRect other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    /// <summary>Grows the rectangle by <paramref name="dx"/>/<paramref name="dy"/> on each side (negative shrinks).</summary>
    public PixelRect Inflate(int dx, int dy) =>
        new(Left - dx, Top - dy, Right + dx, Bottom + dy);
}
