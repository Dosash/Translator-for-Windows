using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Translator.Platform;

namespace Translator.UI.Controls;

/// <summary>
/// The brand mark: a rounded gradient square (sage→sky, sage→rose in the dark theme) with the white
/// tray glyph. Port of macOS <c>LogoMark</c>; colors follow the theme resources live.
/// </summary>
public sealed class LogoMark : FrameworkElement
{
    private static readonly Geometry Glyph = TrayGlyph.CreateGeometry();

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(LogoMark),
        new FrameworkPropertyMetadata(34.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnGlowChanged));

    public static readonly DependencyProperty StartColorProperty = DependencyProperty.Register(
        nameof(StartColor), typeof(Color), typeof(LogoMark),
        new FrameworkPropertyMetadata(Colors.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EndColorProperty = DependencyProperty.Register(
        nameof(EndColor), typeof(Color), typeof(LogoMark),
        new FrameworkPropertyMetadata(Colors.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlowColorProperty = DependencyProperty.Register(
        nameof(GlowColor), typeof(Color), typeof(LogoMark),
        new FrameworkPropertyMetadata(Colors.Transparent, OnGlowChanged));

    private readonly DropShadowEffect _shadow = new() { Direction = 270, ShadowDepth = 2, BlurRadius = 6 };

    public LogoMark()
    {
        SetResourceReference(StartColorProperty, "SageColor");
        SetResourceReference(EndColorProperty, "LogoEndColor");
        SetResourceReference(GlowColorProperty, "SecondaryGlowColor");
        Effect = _shadow;
        SnapsToDevicePixels = true;
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Color StartColor
    {
        get => (Color)GetValue(StartColorProperty);
        set => SetValue(StartColorProperty, value);
    }

    public Color EndColor
    {
        get => (Color)GetValue(EndColorProperty);
        set => SetValue(EndColorProperty, value);
    }

    public Color GlowColor
    {
        get => (Color)GetValue(GlowColorProperty);
        set => SetValue(GlowColorProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var side = Size;
        var fill = new LinearGradientBrush(StartColor, EndColor, new Point(0, 0), new Point(1, 1));
        fill.Freeze();
        var radius = side * 0.29;
        drawingContext.DrawRoundedRectangle(fill, null, new Rect(0, 0, side, side), radius, radius);

        var glyphSide = side * 0.62;
        var offset = (side - glyphSide) / 2;
        drawingContext.PushTransform(new TranslateTransform(offset, offset));
        drawingContext.PushTransform(new ScaleTransform(glyphSide / 18.0, glyphSide / 18.0));
        drawingContext.DrawGeometry(Brushes.White, null, Glyph);
        drawingContext.Pop();
        drawingContext.Pop();
    }

    private static void OnGlowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var logo = (LogoMark)d;
        var glow = logo.GlowColor;
        logo._shadow.Color = Color.FromRgb(glow.R, glow.G, glow.B);
        logo._shadow.Opacity = glow.A / 255.0;
        // macOS: radius 8 (dark) / 3 (light); WPF blur radius is roughly twice a SwiftUI shadow radius.
        logo._shadow.BlurRadius = ThemeManager.Palette.IsDark ? 14 : 6;
    }
}
