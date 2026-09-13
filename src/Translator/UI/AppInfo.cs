using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Translator.UI.Interop;

namespace Translator.UI;

public static class AppInfo
{
    public const string AppId = "Translator";

    /// <summary>Informational version without "+commit" build metadata.</summary>
    public static string Version { get; } = ReadVersion();

    private static readonly Lazy<ImageSource?> LazyIcon = new(LoadIcon);

    /// <summary>The icon embedded in Translator.exe (Assets/AppIcon.ico via ApplicationIcon), or null when there is none.</summary>
    public static ImageSource? Icon => LazyIcon.Value;

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var raw = !string.IsNullOrWhiteSpace(info) ? info : assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var plus = raw.IndexOf('+');
        return plus >= 0 ? raw[..plus] : raw;
    }

    private static ImageSource? LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            var hIcon = UiNativeMethods.ExtractIcon(IntPtr.Zero, path, 0);
            // ExtractIcon returns 1 for files that aren't executables/icons, 0 when there is no icon.
            if (hIcon == IntPtr.Zero || hIcon == 1)
            {
                return null;
            }
            try
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally
            {
                UiNativeMethods.DestroyIcon(hIcon);
            }
        }
        catch
        {
            return null;
        }
    }
}
