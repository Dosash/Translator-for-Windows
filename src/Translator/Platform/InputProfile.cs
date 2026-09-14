using System.IO;
using System.Text;

namespace Translator.Platform;

/// <summary>
/// Which keys copy/paste the selection in a given foreground window. Ctrl+C is the interrupt key in
/// terminals: with nothing selected it kills the running command instead of copying. Terminals (and
/// IDEs with an embedded terminal, where a single top-level window can't tell us which pane has focus)
/// use Ctrl+Insert/Shift+Insert instead — neither is ever delivered to a shell as a signal.
/// </summary>
public enum CopyPasteChord
{
    /// <summary>Ctrl+C / Ctrl+V — ordinary apps.</summary>
    CtrlCV,

    /// <summary>Ctrl+Insert / Shift+Insert — terminals, and editors/IDEs with an embedded terminal.</summary>
    InsertChord,

    /// <summary>
    /// Selecting already copies (PuTTY/KiTTY); no copy keystroke is sent, the clipboard is read as-is.
    /// Paste still uses Shift+Insert, since these apps don't bind Ctrl+V to paste.
    /// </summary>
    CopyOnSelect,
}

/// <summary>Classifies the foreground window by process image name and window class name.</summary>
public static class InputProfile
{
    private sealed record Rule(CopyPasteChord Chord, string[] ProcessNames, string[] ClassNames);

    // Process names are matched case-insensitively; a trailing '*' matches as a prefix. Either the
    // process name or the class name matching is enough — some hosts (e.g. Cmder relaunching ConEmu)
    // are more reliably identified by class than by executable.
    private static readonly Rule[] Rules =
    [
        // PuTTY/KiTTY copy the instant the mouse selects text; there is nothing useful to send for
        // "copy" (Ctrl-C would be forwarded to the remote session as an interrupt).
        new(CopyPasteChord.CopyOnSelect,
            ProcessNames: ["putty.exe", "kitty.exe"],
            ClassNames: ["PuTTY", "KiTTY"]),

        // Terminals, plus IDEs/editors that ship an embedded terminal in the same top-level window.
        // Ctrl+Insert/Shift+Insert works as copy/paste both in a text editor and in a terminal pane,
        // so this is safe regardless of which one currently has focus.
        new(CopyPasteChord.InsertChord,
            ProcessNames:
            [
                "windowsterminal.exe", "mintty.exe", "conemu*", "cmder.exe",
                "alacritty.exe", "wezterm-gui.exe", "tabby.exe", "hyper.exe", "windterm.exe",
                "fluentterminal.exe", "mobaxterm.exe",
                "code.exe", "cursor.exe", "windsurf.exe", "devenv.exe", "zed.exe", "fleet.exe",
                "idea64.exe", "pycharm64.exe", "rider64.exe", "webstorm64.exe", "clion64.exe",
                "goland64.exe", "phpstorm64.exe", "rustrover64.exe", "datagrip64.exe", "studio64.exe",
            ],
            ClassNames: ["CASCADIA_HOSTING_WINDOW_CLASS", "ConsoleWindowClass", "mintty", "VirtualConsoleClass"]),
    ];

    /// <summary>Pure lookup, table-driven: everything unmatched keeps today's Ctrl+C / Ctrl+V.</summary>
    public static CopyPasteChord Classify(string? processName, string? className)
    {
        foreach (var rule in Rules)
        {
            if (MatchesAny(processName, rule.ProcessNames) || MatchesAny(className, rule.ClassNames))
            {
                return rule.Chord;
            }
        }
        return CopyPasteChord.CtrlCV;
    }

    /// <summary>Resolves the foreground window's process/class and classifies it. Best-effort: any
    /// lookup failure (access denied, window gone) falls back to plain Ctrl+C / Ctrl+V.</summary>
    public static CopyPasteChord ForWindow(IntPtr hwnd) => Classify(TryGetProcessName(hwnd), TryGetClassName(hwnd));

    private static bool MatchesAny(string? value, string[] candidates)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        foreach (var candidate in candidates)
        {
            var isPrefix = candidate.EndsWith('*');
            var pattern = isPrefix ? candidate[..^1] : candidate;
            var matches = isPrefix
                ? value.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)
                : value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            if (matches)
            {
                return true;
            }
        }
        return false;
    }

    private static string? TryGetProcessName(IntPtr hwnd)
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
            return null;
        }
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            if (!NativeMethods.QueryFullProcessImageNameW(hProcess, 0, buffer, ref size))
            {
                return null;
            }
            var path = buffer.ToString(0, (int)size);
            return path.Length == 0 ? null : Path.GetFileName(path);
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    private static string? TryGetClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }
        var buffer = new StringBuilder(256);
        var length = NativeMethods.GetClassNameW(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : null;
    }
}
