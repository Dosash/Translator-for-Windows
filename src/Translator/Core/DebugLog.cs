using System.Globalization;
using System.IO;

namespace Translator.Core;

/// <summary>
/// Diagnostic log in %LOCALAPPDATA%\Translator\logs\debug.log.
/// Never write translated text here — only lengths, states and errors.
/// </summary>
public static class DebugLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock Gate = new();

    public static void Write(string message)
    {
        try
        {
            var path = Path.Combine(AppPaths.LogsDirectory, "debug.log");
            var line = $"{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}";
            lock (Gate)
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(path, path + ".old", overwrite: true);
                }
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }
}
