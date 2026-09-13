using System.IO;

namespace Translator.Core;

/// <summary>Where the app keeps its files. Directories are created on first access.</summary>
public static class AppPaths
{
    /// <summary>%APPDATA%\Translator — settings and history (roams with the profile).</summary>
    public static string RoamingDirectory => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Translator"));

    /// <summary>%LOCALAPPDATA%\Translator — large machine-local data.</summary>
    public static string LocalDirectory => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Translator"));

    public static string SettingsFile => Path.Combine(RoamingDirectory, "settings.json");

    public static string HistoryFile => Path.Combine(RoamingDirectory, "history.json");

    public static string ModelsDirectory => Ensure(Path.Combine(LocalDirectory, "models"));

    public static string LogsDirectory => Ensure(Path.Combine(LocalDirectory, "logs"));

    private static string Ensure(string directory)
    {
        Directory.CreateDirectory(directory);
        return directory;
    }
}
