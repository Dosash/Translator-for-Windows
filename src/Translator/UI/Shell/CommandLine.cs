using Translator.Core;
using Translator.Platform;

namespace Translator.UI.Shell;

public enum CliCommandKind
{
    /// <summary>Normal start (first-run window once).</summary>
    Normal,
    /// <summary>Silent start at sign-in.</summary>
    Autostart,
    /// <summary>Open the panel and translate <see cref="CliCommand.Text"/>.</summary>
    Translate,
    /// <summary>Open <see cref="CliCommand.Window"/> (starting the app if needed).</summary>
    Open,
    /// <summary>Ask the running instance to exit.</summary>
    Quit,
    TestTranslate,
    TestOffline,
    /// <summary>A known flag with missing arguments; <see cref="CliCommand.Error"/> says what's wrong.</summary>
    Invalid,
}

/// <summary>Windows reachable with <c>--open</c>.</summary>
public enum AppWindowKind
{
    Panel,
    Settings,
    History,
    Offline,
    FirstRun,
}

public sealed record CliCommand(
    CliCommandKind Kind,
    string? Text = null,
    string? Source = null,
    string? Target = null,
    string? Error = null,
    AppWindowKind? Window = null);

/// <summary>Parses the command line described in docs/ARCHITECTURE.md. Pure, unit-tested.</summary>
public static class CommandLine
{
    public const string TranslateFlag = "--translate";
    public const string OpenFlag = "--open";
    public const string QuitFlag = "--quit";
    public const string TestTranslateFlag = "--test-translate";
    public const string TestOfflineFlag = "--test-offline";

    private static readonly Dictionary<string, AppWindowKind> WindowNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["panel"] = AppWindowKind.Panel,
        ["settings"] = AppWindowKind.Settings,
        ["history"] = AppWindowKind.History,
        ["offline"] = AppWindowKind.Offline,
        ["firstrun"] = AppWindowKind.FirstRun,
    };

    public static CliCommand Parse(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var rest = args.Skip(i + 1).ToList();
            switch (arg.ToLowerInvariant())
            {
                case TestTranslateFlag:
                    return rest.Count >= 1 && !string.IsNullOrWhiteSpace(rest[0])
                        ? new CliCommand(CliCommandKind.TestTranslate, rest[0])
                        : Invalid($"Usage: Translator.exe {TestTranslateFlag} \"text\"");
                case TestOfflineFlag:
                    return rest.Count switch
                    {
                        >= 3 => new CliCommand(CliCommandKind.TestOffline, rest[0], NormalizeSource(rest[1]), rest[2]),
                        2 => new CliCommand(CliCommandKind.TestOffline, rest[0], null, rest[1]),
                        _ => Invalid($"Usage: Translator.exe {TestOfflineFlag} \"text\" [source] target"),
                    };
                case TranslateFlag:
                    return rest.Count >= 1
                        ? new CliCommand(CliCommandKind.Translate, rest[0])
                        : Invalid($"Usage: Translator.exe {TranslateFlag} \"text\"");
                case OpenFlag:
                    return rest.Count >= 1 && WindowNames.TryGetValue(rest[0].Trim(), out var window)
                        ? new CliCommand(CliCommandKind.Open, Window: window)
                        : Invalid($"Usage: Translator.exe {OpenFlag} {string.Join('|', WindowNames.Keys)}");
                case QuitFlag:
                    return new CliCommand(CliCommandKind.Quit);
                case Autostart.LaunchArgument:
                    return new CliCommand(CliCommandKind.Autostart);
            }
        }
        return new CliCommand(CliCommandKind.Normal);
    }

    /// <summary>Port of macOS <c>TranslationService.detectDirection</c>: Cyrillic → ru→en, anything else → en→ru.</summary>
    public static (string Source, string Target) DetectDirection(string text) =>
        AppLanguage.HasCyrillic(text) ? ("ru", "en") : ("en", "ru");

    private static string? NormalizeSource(string source) =>
        string.Equals(source, AppLanguage.AutoCode, StringComparison.OrdinalIgnoreCase) ? null : source;

    private static CliCommand Invalid(string error) => new(CliCommandKind.Invalid, Error: error);
}
