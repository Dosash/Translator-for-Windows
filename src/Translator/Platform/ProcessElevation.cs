using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Translator.Platform;

/// <summary>
/// Whether a process runs elevated. Used to warn the user that selection grabbing can't reach
/// admin windows unless Translator also runs elevated (Windows blocks input simulation across the UAC boundary).
/// </summary>
public static class ProcessElevation
{
    public static bool IsCurrentProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Null when it can't be determined. Access-denied opening the process is treated as elevated —
    /// that's what happens when we're not elevated and the target is.
    /// </summary>
    public static bool? IsWindowElevated(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return null;
        }

        var hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProcess == IntPtr.Zero)
        {
            return true;
        }
        try
        {
            if (!NativeMethods.OpenProcessToken(hProcess, NativeMethods.TOKEN_QUERY, out var hToken))
            {
                return null;
            }
            try
            {
                var size = Marshal.SizeOf<int>();
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!NativeMethods.GetTokenInformation(hToken, NativeMethods.TokenElevation, buffer, size, out _))
                    {
                        return null;
                    }
                    return Marshal.ReadInt32(buffer) != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(hToken);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }
}
