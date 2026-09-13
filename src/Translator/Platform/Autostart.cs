namespace Translator.Platform;

/// <summary>
/// "Launch at sign-in" via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
/// <remarks>Stub: the real implementation replaces the bodies, keeping this public surface.</remarks>
public static class Autostart
{
    /// <summary>Command-line flag added to the Run entry so the app starts silently.</summary>
    public const string LaunchArgument = "--autostart";

    public static bool IsEnabled() => false;

    /// <summary>Throws on failure (the caller shows the message).</summary>
    public static void SetEnabled(bool enabled)
    {
    }
}
