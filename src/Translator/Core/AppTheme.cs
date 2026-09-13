namespace Translator.Core;

public enum AppTheme
{
    CalmGlass,
    NeonGlass,
    FrostGlass,
}

public static class AppThemeInfo
{
    public static AppTheme Parse(string? value) => value switch
    {
        nameof(AppTheme.NeonGlass) or "neonGlass" or "liquidGlass" => AppTheme.NeonGlass,
        nameof(AppTheme.FrostGlass) or "frostGlass" or "mindora" => AppTheme.FrostGlass,
        _ => AppTheme.CalmGlass,
    };

    public static string DisplayName(this AppTheme theme) => theme switch
    {
        AppTheme.NeonGlass => "Neon Glass",
        AppTheme.FrostGlass => "Frost Glass",
        _ => "Calm Glass",
    };

    public static bool IsDark(this AppTheme theme) => theme == AppTheme.NeonGlass;

    /// <summary>L10n key of the short theme description.</summary>
    public static string CaptionKey(this AppTheme theme) => theme switch
    {
        AppTheme.NeonGlass => "theme.neon.caption",
        AppTheme.FrostGlass => "theme.frost.caption",
        _ => "theme.calm.caption",
    };
}
