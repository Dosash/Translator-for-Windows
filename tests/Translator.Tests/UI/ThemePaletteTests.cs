using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Translator.Core;
using Translator.UI;

namespace Translator.Tests.UI;

public class ThemePaletteTests
{
    private static readonly Color Accent = Color.FromRgb(0x00, 0x78, 0xD4);

    [Fact]
    public void CalmGlass_uses_the_fixed_green_accent_whatever_the_system_accent()
    {
        var palette = ThemePalette.Create(AppTheme.CalmGlass, Accent);

        Assert.Equal(ThemePalette.Hex(0x28A97A), palette.Sage);
        Assert.Equal(ThemePalette.Hex(0x1E8D66), palette.SageDeep);
        Assert.Equal(ThemePalette.Hex(0x7FA8DD), palette.Sky);
        Assert.Equal(ThemePalette.Hex(0x1E2A32), palette.Ink);
        Assert.Equal(ThemePalette.Hex(0xDDF2EA), palette.SelectedControlFill);
        Assert.False(palette.IsDark);
    }

    [Theory]
    [InlineData(AppTheme.NeonGlass)]
    [InlineData(AppTheme.FrostGlass)]
    public void NeonAndFrost_follow_the_system_accent(AppTheme theme)
    {
        var palette = ThemePalette.Create(theme, Accent);

        Assert.Equal(Accent, palette.Sage);
        Assert.Equal(Accent, palette.SageDeep);
        Assert.Equal(Accent.R, palette.Sky.R);
    }

    [Fact]
    public void Sky_is_the_accent_with_theme_specific_opacity()
    {
        Assert.Equal(224, ThemePalette.Create(AppTheme.NeonGlass, Accent).Sky.A); // 0.88
        Assert.Equal(199, ThemePalette.Create(AppTheme.FrostGlass, Accent).Sky.A); // 0.78
    }

    [Fact]
    public void NeonGlass_is_dark_with_light_ink()
    {
        var palette = ThemePalette.Create(AppTheme.NeonGlass, Accent);

        Assert.True(palette.IsDark);
        Assert.Equal(ThemePalette.Hex(0xF5FBFF), palette.Ink);
        Assert.Equal(ThemePalette.Hex(0xFFB4E8), palette.RoseInk);
        Assert.Equal([ThemePalette.Hex(0x06111D), ThemePalette.Hex(0x111A31), ThemePalette.Hex(0x210B2E)], palette.Background);
        Assert.Equal(ThemePalette.WithOpacity(Colors.White, 0.10), palette.CardFill);
    }

    [Fact]
    public void FrostGlass_ports_the_light_palette()
    {
        var palette = ThemePalette.Create(AppTheme.FrostGlass, Accent);

        Assert.Equal(ThemePalette.Hex(0x111827), palette.Ink);
        Assert.Equal(ThemePalette.Hex(0xFF80B6), palette.Rose);
        Assert.Equal(ThemePalette.Hex(0x5B6472), palette.SecondaryText);
        Assert.Equal(ThemePalette.WithOpacity(Accent, 0.14), palette.SelectedControlFill);
        Assert.Equal([ThemePalette.Hex(0xF7FAFD), ThemePalette.Hex(0xE2E9F1), ThemePalette.Hex(0xCAD4DF)], palette.Background);
    }

    [Fact]
    public void System_accent_alpha_is_ignored()
    {
        var translucent = Color.FromArgb(0, Accent.R, Accent.G, Accent.B);

        Assert.Equal(255, ThemePalette.Create(AppTheme.FrostGlass, translucent).Sage.A);
        Assert.Equal(255, ThemePalette.PreviewAccent(AppTheme.NeonGlass, translucent).A);
    }

    [Fact]
    public void Logo_gradient_ends_in_rose_when_dark_and_sky_when_light()
    {
        var neon = ThemePalette.Create(AppTheme.NeonGlass, Accent);
        var calm = ThemePalette.Create(AppTheme.CalmGlass, Accent);

        Assert.Equal(neon.Rose, neon.ResourceColors()["LogoEnd"]);
        Assert.Equal(calm.Sky, calm.ResourceColors()["LogoEnd"]);
    }

    [Fact]
    public void PreviewAccent_is_fixed_for_calm_and_system_for_others()
    {
        Assert.Equal(ThemePalette.CalmAccent, ThemePalette.PreviewAccent(AppTheme.CalmGlass, Accent));
        Assert.Equal(Accent, ThemePalette.PreviewAccent(AppTheme.FrostGlass, Accent));
    }

    [Fact]
    public void Mix_and_opacity_helpers()
    {
        Assert.Equal(Color.FromArgb(128, 10, 20, 30), ThemePalette.WithOpacity(Color.FromRgb(10, 20, 30), 0.5));
        Assert.Equal(Color.FromRgb(128, 128, 128), ThemePalette.Mix(Colors.Black, Colors.White, 0.5));
        Assert.Equal(Colors.Black, ThemePalette.Mix(Colors.Black, Colors.White, -1));
    }

    [Fact]
    public void Every_theme_resource_referenced_by_the_UI_is_provided()
    {
        var root = FindRepositoryRoot();
        var appDirectory = Path.Combine(root, "src", "Translator");
        var files = Directory.GetFiles(appDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        var sources = files.Select(File.ReadAllText).ToList();

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, @"\{DynamicResource\s+([A-Za-z]+)\}"))
            {
                referenced.Add(match.Groups[1].Value);
            }
            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                // Resource keys passed to SetResourceReference from code.
                foreach (Match match in Regex.Matches(text, "\"([A-Za-z]+(?:Brush|Color|FontFamily))\""))
                {
                    referenced.Add(match.Groups[1].Value);
                }
            }
        }

        var defined = new HashSet<string>(StringComparer.Ordinal)
        {
            "BackgroundOpaqueBrush", "BackgroundAcrylicBrush", "BackgroundMicaBrush",
        };
        foreach (var text in sources)
        {
            foreach (Match match in Regex.Matches(text, "x:Key=\"([A-Za-z]+)\""))
            {
                defined.Add(match.Groups[1].Value);
            }
        }
        foreach (var name in ThemePalette.Create(AppTheme.CalmGlass, Accent).ResourceColors().Keys)
        {
            defined.Add(name + "Color");
            defined.Add(name + "Brush");
        }

        var missing = referenced.Where(key => !defined.Contains(key)).OrderBy(key => key).ToList();
        Assert.True(referenced.Count > 20, "The scan should find the theme resources used by the UI.");
        Assert.Empty(missing);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Translator.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Repository root (Translator.slnx) not found.");
    }
}
