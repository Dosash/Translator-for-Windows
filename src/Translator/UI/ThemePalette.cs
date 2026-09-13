using System.Windows.Media;
using Translator.Core;

namespace Translator.UI;

/// <summary>
/// Colors of one theme, ported from macOS <c>Theme.swift</c>. Pure data: <see cref="ThemeManager"/>
/// turns it into brush resources. Calm Glass has a fixed green accent; Neon/Frost follow the system accent.
/// </summary>
public sealed class ThemePalette
{
    public static readonly Color CalmAccent = Hex(0x28A97A);

    /// <summary>Alpha of the theme gradient drawn over a system backdrop (Acrylic for panel/bubble, Mica for windows).</summary>
    public const double AcrylicGradientOpacity = 0.72;
    public const double MicaGradientOpacity = 0.66;

    private ThemePalette(AppTheme theme) => Theme = theme;

    public AppTheme Theme { get; }
    public bool IsDark => Theme.IsDark();

    public Color Sage { get; private init; }
    public Color SageDeep { get; private init; }
    public Color Sky { get; private init; }
    public Color Ink { get; private init; }
    public Color Mist { get; private init; }
    public Color MistDeep { get; private init; }
    public Color Rose { get; private init; }
    public Color RoseInk { get; private init; }
    public IReadOnlyList<Color> Background { get; private init; } = [];
    public Color CardFill { get; private init; }
    public Color CardStroke { get; private init; }
    public Color GlassTint { get; private init; }
    public Color Glow { get; private init; }
    public Color SecondaryGlow { get; private init; }
    public Color ControlFill { get; private init; }
    public Color SelectedControlFill { get; private init; }
    public Color ControlStroke { get; private init; }
    public Color SecondaryText { get; private init; }

    public static ThemePalette Create(AppTheme theme, Color systemAccent)
    {
        systemAccent.A = 255;
        var calm = theme == AppTheme.CalmGlass;
        var dark = theme.IsDark();
        var white = Colors.White;

        return new ThemePalette(theme)
        {
            Sage = calm ? CalmAccent : systemAccent,
            SageDeep = calm ? Hex(0x1E8D66) : systemAccent,
            Sky = calm ? Hex(0x7FA8DD) : WithOpacity(systemAccent, dark ? 0.88 : 0.78),
            Ink = calm ? Hex(0x1E2A32) : dark ? Hex(0xF5FBFF) : Hex(0x111827),
            Mist = calm ? Hex(0xEFF7F4) : dark ? Hex(0x07111E) : Hex(0xEEF3F8),
            MistDeep = calm ? Hex(0xDDEBE6) : dark ? Hex(0x130A24) : Hex(0xD5DEE8),
            Rose = calm ? Hex(0xE88B8B) : dark ? Hex(0xFF58D2) : Hex(0xFF80B6),
            RoseInk = dark ? Hex(0xFFB4E8) : Hex(0xB44463),
            Background = calm
                ? [Hex(0xF8FBFA), Hex(0xEEF7F4), Hex(0xEAF2F8)]
                : dark
                    ? [Hex(0x06111D), Hex(0x111A31), Hex(0x210B2E)]
                    : [Hex(0xF7FAFD), Hex(0xE2E9F1), Hex(0xCAD4DF)],
            CardFill = calm ? WithOpacity(white, 0.68) : WithOpacity(white, dark ? 0.10 : 0.42),
            CardStroke = calm ? WithOpacity(white, 0.86) : WithOpacity(white, dark ? 0.26 : 0.74),
            GlassTint = calm ? WithOpacity(Hex(0xEAF6F2), 0.34) : dark ? WithOpacity(systemAccent, 0.16) : WithOpacity(white, 0.24),
            Glow = calm ? WithOpacity(Hex(0x9BB9AE), 0.22) : dark ? WithOpacity(systemAccent, 0.46) : WithOpacity(Colors.Black, 0.14),
            SecondaryGlow = calm ? WithOpacity(Hex(0x8FCAB9), 0.34) : dark ? WithOpacity(systemAccent, 0.34) : WithOpacity(white, 0.7),
            ControlFill = calm ? WithOpacity(white, 0.78) : WithOpacity(white, dark ? 0.12 : 0.70),
            SelectedControlFill = calm ? Hex(0xDDF2EA) : WithOpacity(systemAccent, dark ? 0.24 : 0.14),
            ControlStroke = calm ? WithOpacity(Hex(0xDBE8E3), 0.88) : dark ? WithOpacity(white, 0.18) : WithOpacity(white, 0.82),
            SecondaryText = calm ? Hex(0x7A8790) : dark ? WithOpacity(white, 0.64) : Hex(0x5B6472),
        };
    }

    /// <summary>
    /// Every solid color the XAML uses, by resource name (exposed as "{name}Color" and "{name}Brush").
    /// Includes Windows-specific derived states (hover, pressed, menus, scrollbars) the macOS styles don't need.
    /// </summary>
    public IReadOnlyDictionary<string, Color> ResourceColors()
    {
        var white = Colors.White;
        var menuBackground = IsDark ? Hex(0x121A2C) : Mix(Background[0], Background[1], 0.5);
        return new Dictionary<string, Color>
        {
            ["Sage"] = Sage,
            ["SageDeep"] = SageDeep,
            ["Sky"] = Sky,
            ["Ink"] = Ink,
            ["Mist"] = Mist,
            ["MistDeep"] = MistDeep,
            ["Rose"] = Rose,
            ["RoseInk"] = RoseInk,
            ["CardFill"] = CardFill,
            ["CardStroke"] = CardStroke,
            ["GlassTint"] = GlassTint,
            ["Glow"] = Glow,
            ["SecondaryGlow"] = SecondaryGlow,
            ["ControlFill"] = ControlFill,
            ["SelectedControlFill"] = SelectedControlFill,
            ["ControlStroke"] = ControlStroke,
            ["SecondaryText"] = SecondaryText,

            ["InkStrong"] = WithOpacity(Ink, 0.70),
            ["InkNear"] = WithOpacity(Ink, 0.80),
            ["InkMuted"] = WithOpacity(Ink, 0.55),
            ["InkCaption"] = WithOpacity(Ink, 0.50),
            ["InkSubtle"] = WithOpacity(Ink, 0.45),
            ["InkFaint"] = WithOpacity(Ink, 0.40),
            ["InkGhost"] = WithOpacity(Ink, 0.25),
            ["SageHover"] = Mix(Sage, white, IsDark ? 0.18 : 0.14),
            ["SageSoft"] = WithOpacity(Sage, 0.16),
            ["PressedFill"] = IsDark ? Mix(Sage, Colors.Black, 0.35) : Ink,
            ["HoverOverlay"] = WithOpacity(white, IsDark ? 0.16 : 0.18),
            ["RoseSoft"] = WithOpacity(Rose, 0.28),
            ["RoseRecording"] = WithOpacity(Rose, 0.25),
            ["Divider"] = IsDark ? WithOpacity(white, 0.12) : WithOpacity(Ink, 0.09),
            ["MenuBackground"] = menuBackground,
            ["MenuStroke"] = IsDark ? WithOpacity(white, 0.16) : WithOpacity(Ink, 0.12),
            ["MenuHover"] = IsDark ? WithOpacity(white, 0.10) : Theme == AppTheme.CalmGlass ? SelectedControlFill : WithOpacity(Sage, 0.12),
            ["GhostHover"] = IsDark ? WithOpacity(white, 0.10) : WithOpacity(Ink, 0.07),
            ["ScrollThumb"] = WithOpacity(Ink, 0.22),
            ["ScrollThumbHover"] = WithOpacity(Ink, 0.42),
            ["FocusStroke"] = WithOpacity(Ink, 0.9),
            ["ToggleOffTrack"] = IsDark ? WithOpacity(white, 0.08) : WithOpacity(white, 0.6),
            ["ToggleOffStroke"] = WithOpacity(Ink, 0.45),
            ["ToggleOffThumb"] = WithOpacity(Ink, 0.62),
            ["LogoEnd"] = IsDark ? Rose : Sky,
            ["Shadow"] = IsDark ? WithOpacity(Colors.Black, 0.5) : WithOpacity(Hex(0x1E2A32), 0.18),
        };
    }

    /// <summary>The swatch gradient shown for a theme in the settings cards (independent of the active theme).</summary>
    public static IReadOnlyList<Color> PreviewColors(AppTheme theme) => theme switch
    {
        AppTheme.NeonGlass => [Hex(0x0A1524), Hex(0x28113A)],
        AppTheme.FrostGlass => [Hex(0xF7FAFD), Hex(0xD8E2EC)],
        _ => [Hex(0xF8FBFA), Hex(0xEAF6F2), Hex(0xEAF2F8)],
    };

    public static Color PreviewAccent(AppTheme theme, Color systemAccent)
    {
        systemAccent.A = 255;
        return theme == AppTheme.CalmGlass ? CalmAccent : systemAccent;
    }

    public static Color Hex(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Color WithOpacity(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), color.R, color.G, color.B);

    /// <summary>Linear blend of opaque RGB channels; <paramref name="amount"/> 0 = <paramref name="from"/>, 1 = <paramref name="to"/>.</summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * Math.Clamp(amount, 0, 1));
        return Color.FromArgb(Lerp(from.A, to.A), Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
    }
}
