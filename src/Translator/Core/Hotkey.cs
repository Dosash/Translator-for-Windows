using System.Windows.Input;

namespace Translator.Core;

/// <summary>A global shortcut. <see cref="None"/> means "disabled".</summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public static readonly Hotkey None = default;

    public static Hotkey DefaultSelection { get; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.T);
    public static Hotkey DefaultClipboard { get; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.C);
    public static Hotkey DefaultPanel { get; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Space);
    public static Hotkey DefaultScreen { get; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.S);

    public bool IsNone => Key == Key.None;

    /// <summary>Requires Ctrl, Alt or Win so plain typing is never captured.</summary>
    public bool IsValid =>
        !IsNone && (Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;

    /// <summary>Human-readable form, e.g. "Ctrl+Alt+T". Also the persisted form.</summary>
    public string Display
    {
        get
        {
            if (IsNone)
            {
                return string.Empty;
            }
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(KeyName(Key));
            return string.Join("+", parts);
        }
    }

    public override string ToString() => Display;

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        var modifiers = ModifierKeys.None;
        Key key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (key != Key.None || !TryParseKey(raw, out key))
                    {
                        return false;
                    }
                    break;
            }
        }
        if (key == Key.None)
        {
            return false;
        }
        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
        Key.Space => "Space",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.Escape => "Esc",
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemBackslash or Key.OemPipe => "\\",
        _ => key.ToString(),
    };

    private static bool TryParseKey(string text, out Key key)
    {
        foreach (var candidate in Enum.GetValues<Key>())
        {
            if (candidate != Key.None && string.Equals(KeyName(candidate), text, StringComparison.OrdinalIgnoreCase))
            {
                key = candidate;
                return true;
            }
        }
        return Enum.TryParse(text, ignoreCase: true, out key) && key != Key.None;
    }
}
