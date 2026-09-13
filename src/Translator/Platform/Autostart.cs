using Microsoft.Win32;
using Translator.Core;

namespace Translator.Platform;

/// <summary>
/// "Launch at sign-in" via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public static class Autostart
{
    /// <summary>Command-line flag added to the Run entry so the app starts silently.</summary>
    public const string LaunchArgument = "--autostart";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Translator";

    public static bool IsEnabled() => IsEnabled(RunKeyPath, ApprovedKeyPath, ValueName, CurrentExePath());

    /// <summary>Throws on failure (the caller shows the message).</summary>
    public static void SetEnabled(bool enabled) => SetEnabled(enabled, RunKeyPath, ApprovedKeyPath, ValueName, CurrentExePath());

    /// <summary>True only if the Run value points at this exe and Task Manager's "Startup apps" hasn't disabled it.</summary>
    internal static bool IsEnabled(string runKeyPath, string approvedKeyPath, string valueName, string exePath)
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(runKeyPath);
            if (runKey?.GetValue(valueName) is not string command || string.IsNullOrEmpty(command))
            {
                return false;
            }
            if (!string.Equals(ExtractExePath(command), exePath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            using var approvedKey = Registry.CurrentUser.OpenSubKey(approvedKeyPath);
            if (approvedKey?.GetValue(valueName) is byte[] { Length: > 0 } state && state[0] == 0x03)
            {
                return false; // disabled via Settings/Task Manager "Startup apps"
            }
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Autostart.IsEnabled failed: {ex.GetType().Name}");
            return false;
        }
    }

    internal static void SetEnabled(bool enabled, string runKeyPath, string approvedKeyPath, string valueName, string exePath)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(runKeyPath)
            ?? throw new InvalidOperationException($"Couldn't open {runKeyPath}");

        if (!enabled)
        {
            runKey.DeleteValue(valueName, throwOnMissingValue: false);
            return;
        }

        runKey.SetValue(valueName, $"\"{exePath}\" {LaunchArgument}");

        using var approvedKey = Registry.CurrentUser.OpenSubKey(approvedKeyPath, writable: true);
        if (approvedKey?.GetValue(valueName) is byte[] { Length: > 0 } state && state[0] == 0x03)
        {
            var reEnabled = (byte[])state.Clone();
            reEnabled[0] = 0x02;
            approvedKey.SetValue(valueName, reEnabled, RegistryValueKind.Binary);
        }
    }

    private static string CurrentExePath() =>
        Environment.ProcessPath ?? throw new InvalidOperationException("Environment.ProcessPath is unavailable");

    private static string ExtractExePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : command.Trim('"');
        }
        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }
}
