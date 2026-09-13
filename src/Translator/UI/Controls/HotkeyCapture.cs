using System.Windows.Input;
using Translator.Core;

namespace Translator.UI.Controls;

public enum HotkeyCaptureAction
{
    /// <summary>Keep recording (lone modifier, or a combination without Ctrl/Alt/Win).</summary>
    Ignore,
    Cancel,
    /// <summary>Turn the shortcut off (<see cref="Hotkey.None"/>).</summary>
    Clear,
    Accept,
}

public readonly record struct HotkeyCaptureResult(HotkeyCaptureAction Action, Hotkey Hotkey = default);

/// <summary>Interprets key presses while <see cref="HotkeyRecorder"/> is recording. Pure, unit-tested.</summary>
public static class HotkeyCapture
{
    /// <summary>WPF reports Alt combinations as <see cref="Key.System"/> and IME/dead keys indirectly.</summary>
    public static Key ResolveKey(Key key, Key systemKey, Key imeProcessedKey, Key deadCharProcessedKey) => key switch
    {
        Key.System => systemKey,
        Key.ImeProcessed => imeProcessedKey,
        Key.DeadCharProcessed => deadCharProcessedKey,
        _ => key,
    };

    public static HotkeyCaptureResult Interpret(Key key, ModifierKeys modifiers)
    {
        if (IsModifierOrUnusable(key))
        {
            return new(HotkeyCaptureAction.Ignore);
        }

        var hasRealModifier = (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;
        if (!hasRealModifier)
        {
            if (key == Key.Escape)
            {
                return new(HotkeyCaptureAction.Cancel);
            }
            if (key is Key.Back or Key.Delete)
            {
                return new(HotkeyCaptureAction.Clear);
            }
        }

        var hotkey = new Hotkey(modifiers, key);
        return hotkey.IsValid ? new(HotkeyCaptureAction.Accept, hotkey) : new(HotkeyCaptureAction.Ignore);
    }

    private static bool IsModifierOrUnusable(Key key) => key is
        Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed
        or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
        or Key.CapsLock or Key.NumLock or Key.Scroll or Key.Apps;
}
