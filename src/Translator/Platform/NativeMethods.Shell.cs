using System.Runtime.InteropServices;

namespace Translator.Platform;

internal static partial class NativeMethods
{
    internal const string PrimaryTaskbarClass = "Shell_TrayWnd";

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr FindTopLevelWindow(string? className, string? windowName);
}
