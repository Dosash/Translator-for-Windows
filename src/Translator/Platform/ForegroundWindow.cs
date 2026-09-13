using Translator.Core;

namespace Translator.Platform;

/// <summary>Reading and robustly switching the foreground window (Windows restricts this by default).</summary>
public static partial class ForegroundWindow
{
    public static IntPtr Get() => NativeMethods.GetForegroundWindow();

    /// <summary>
    /// Brings <paramref name="hwnd"/> to the foreground. Restores it if minimized; if a plain
    /// <c>SetForegroundWindow</c> is refused (the common case — Windows only allows the current
    /// foreground app to switch focus), attaches input queues with the current foreground thread
    /// and retries, falling back to a synthetic key press that satisfies the foreground-lock check.
    /// </summary>
    public static bool Activate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }
        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }
        if (NativeMethods.SetForegroundWindow(hwnd) && Get() == hwnd)
        {
            return true;
        }

        var foregroundHwnd = NativeMethods.GetForegroundWindow();
        var currentThreadId = GetCurrentThreadId();
        var foregroundThreadId = foregroundHwnd == IntPtr.Zero ? 0u : NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);
        var targetThreadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);

        var attachedToForeground = foregroundThreadId != 0 && foregroundThreadId != currentThreadId
            && NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
        var attachedToTarget = targetThreadId != 0 && targetThreadId != currentThreadId
            && NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, true);
        try
        {
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedToForeground)
            {
                NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
            if (attachedToTarget)
            {
                NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }

        if (Get() == hwnd)
        {
            return true;
        }

        // Last resort: a synthetic key event satisfies "the switch was triggered by input" so the
        // foreground-lock timeout doesn't block us.
        SendSyntheticKeyNudge();
        NativeMethods.SetForegroundWindow(hwnd);

        var success = Get() == hwnd;
        if (!success)
        {
            DebugLog.Write("ForegroundWindow.Activate: could not switch foreground window");
        }
        return success;
    }

    public static uint GetProcessId(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return processId;
    }

    private static void SendSyntheticKeyNudge()
    {
        NativeMethods.INPUT Key(int vk, bool down) => new()
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    dwFlags = down ? 0u : NativeMethods.KEYEVENTF_KEYUP,
                },
            },
        };
        var inputs = new[] { Key(NativeMethods.VK_MENU, true), Key(NativeMethods.VK_MENU, false) };
        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
