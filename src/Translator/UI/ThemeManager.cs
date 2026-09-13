using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Translator.Core;
using WinRtUIColorType = Windows.UI.ViewManagement.UIColorType;
using WinRtUISettings = Windows.UI.ViewManagement.UISettings;

namespace Translator.UI;

/// <summary>
/// Applies an <see cref="AppTheme"/> by (re)writing the color/brush resources that all XAML uses via
/// DynamicResource, so a theme or accent change updates every open window live.
/// </summary>
public static class ThemeManager
{
    private static readonly Color FallbackAccent = ThemePalette.Hex(0x0078D4);

    private static WinRtUISettings? _uiSettings;
    private static Dispatcher? _dispatcher;

    /// <summary>Raised on the UI thread after resources were updated (windows re-apply DWM dark mode).</summary>
    public static event EventHandler? ThemeChanged;

    public static AppTheme Current { get; private set; } = AppTheme.CalmGlass;

    public static ThemePalette Palette { get; private set; } = ThemePalette.Create(AppTheme.CalmGlass, FallbackAccent);

    public static Color SystemAccent { get; private set; } = FallbackAccent;

    public static void Initialize(Dispatcher dispatcher)
    {
        if (_dispatcher is not null)
        {
            return;
        }
        _dispatcher = dispatcher;
        try
        {
            // Kept in a static field: the WinRT object must stay alive to keep raising ColorValuesChanged.
            _uiSettings = new WinRtUISettings();
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
            SystemAccent = ReadAccent();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"ThemeManager: UISettings unavailable ({ex.GetType().Name})");
        }
    }

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        Palette = ThemePalette.Create(theme, SystemAccent);
        if (Application.Current is { } app)
        {
            WriteResources(app.Resources, Palette);
        }
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static LinearGradientBrush CreateGradient(IReadOnlyList<Color> colors, double opacity = 1)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (var i = 0; i < colors.Count; i++)
        {
            var offset = colors.Count == 1 ? 0 : (double)i / (colors.Count - 1);
            brush.GradientStops.Add(new GradientStop(ThemePalette.WithOpacity(colors[i], opacity * colors[i].A / 255.0), offset));
        }
        brush.Freeze();
        return brush;
    }

    private static void WriteResources(ResourceDictionary resources, ThemePalette palette)
    {
        foreach (var (name, color) in palette.ResourceColors())
        {
            resources[name + "Color"] = color;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[name + "Brush"] = brush;
        }
        resources["BackgroundOpaqueBrush"] = CreateGradient(palette.Background);
        resources["BackgroundAcrylicBrush"] = CreateGradient(palette.Background, ThemePalette.AcrylicGradientOpacity);
        resources["BackgroundMicaBrush"] = CreateGradient(palette.Background, ThemePalette.MicaGradientOpacity);
    }

    private static Color ReadAccent()
    {
        if (_uiSettings is null)
        {
            return FallbackAccent;
        }
        var value = _uiSettings.GetColorValue(WinRtUIColorType.Accent);
        return Color.FromRgb(value.R, value.G, value.B);
    }

    private static void OnColorValuesChanged(WinRtUISettings sender, object args)
    {
        // Raised on a background thread.
        _dispatcher?.BeginInvoke(() =>
        {
            try
            {
                var accent = ReadAccent();
                if (accent != SystemAccent)
                {
                    SystemAccent = accent;
                    Apply(Current);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"ThemeManager: accent refresh failed ({ex.GetType().Name})");
            }
        });
    }
}
