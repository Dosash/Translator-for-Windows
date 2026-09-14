using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Translator.Core;

/// <summary>Where the app keeps its files. Directories are created on first access.</summary>
public static class AppPaths
{
    /// <summary>When set, settings, history, models and logs all live under this directory (tests, portable use).</summary>
    public const string DataDirectoryVariable = "TRANSLATOR_DATA_DIR";

    /// <summary>The full path from <see cref="DataDirectoryVariable"/>, or null when the standard profile folders are used.</summary>
    public static string? DataDirectoryOverride { get; } = ResolveOverride(Environment.GetEnvironmentVariable(DataDirectoryVariable));

    /// <summary>%APPDATA%\Translator — settings and history (roams with the profile).</summary>
    public static string RoamingDirectory => Ensure(DataDirectoryOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Translator"));

    /// <summary>%LOCALAPPDATA%\Translator — large machine-local data.</summary>
    public static string LocalDirectory => Ensure(DataDirectoryOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Translator"));

    public static string SettingsFile => Path.Combine(RoamingDirectory, "settings.json");

    public static string HistoryFile => Path.Combine(RoamingDirectory, "history.json");

    public static string ModelsDirectory => Ensure(Path.Combine(LocalDirectory, "models"));

    public static string LogsDirectory => Ensure(Path.Combine(LocalDirectory, "logs"));

    internal static string? ResolveOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
    }

    /// <summary>
    /// Single-instance identity: an instance with its own data directory is a separate profile, so it
    /// must not forward its arguments to (or be quit by) the instance that uses the default folders.
    /// </summary>
    internal static string InstanceId(string appId, string? dataDirectoryOverride)
    {
        if (dataDirectoryOverride is null)
        {
            return appId;
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(dataDirectoryOverride.ToUpperInvariant()));
        return $"{appId}-data-{Convert.ToHexStringLower(hash.AsSpan(0, 6))}";
    }

    private static string Ensure(string directory)
    {
        Directory.CreateDirectory(directory);
        return directory;
    }
}
