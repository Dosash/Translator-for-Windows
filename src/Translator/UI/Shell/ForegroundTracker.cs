using Translator.Core;
using Translator.Platform;
using Translator.UI.Interop;

namespace Translator.UI.Shell;

/// <summary>
/// Remembers the last foreground window of another app. Clicking the tray icon moves the foreground to
/// the taskbar, so tray-menu actions ("translate selected text") need to know where the user was.
/// </summary>
internal sealed class ForegroundTracker : IDisposable
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow",
        "Progman",
        "WorkerW",
    };

    private readonly UiNativeMethods.WinEventProc _callback;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private IntPtr _hook;
    private IntPtr _lastExternal;

    public ForegroundTracker()
    {
        _callback = OnForegroundChanged;
        _hook = UiNativeMethods.SetWinEventHook(
            UiNativeMethods.EVENT_SYSTEM_FOREGROUND, UiNativeMethods.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback,
            0, 0, UiNativeMethods.WINEVENT_OUTOFCONTEXT | UiNativeMethods.WINEVENT_SKIPOWNPROCESS);
        if (_hook == IntPtr.Zero)
        {
            DebugLog.Write("ForegroundTracker: SetWinEventHook failed");
        }
        Consider(ForegroundWindow.Get());
    }

    /// <summary>The most recent foreground window of another app that still exists, or zero.</summary>
    public IntPtr LastExternalWindow => _lastExternal != IntPtr.Zero && UiNativeMethods.IsWindow(_lastExternal) ? _lastExternal : IntPtr.Zero;

    public bool IsOwnWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && ForegroundWindow.GetProcessId(hwnd) == _ownProcessId;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UiNativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) =>
        Consider(hwnd);

    private void Consider(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd) || ShellClasses.Contains(UiNativeMethods.GetClassName(hwnd)))
        {
            return;
        }
        _lastExternal = hwnd;
    }
}
