using System.Windows.Input;
using Translator.Core;
using Translator.UI.Controls;

namespace Translator.Tests.UI;

public class HotkeyCaptureTests
{
    [Theory]
    [InlineData(Key.LeftCtrl, ModifierKeys.Control)]
    [InlineData(Key.RightAlt, ModifierKeys.Alt)]
    [InlineData(Key.LeftShift, ModifierKeys.Shift)]
    [InlineData(Key.LWin, ModifierKeys.Windows)]
    [InlineData(Key.System, ModifierKeys.Alt)]
    public void Lone_modifiers_keep_recording(Key key, ModifierKeys modifiers)
    {
        Assert.Equal(HotkeyCaptureAction.Ignore, HotkeyCapture.Interpret(key, modifiers).Action);
    }

    [Fact]
    public void Escape_cancels()
    {
        Assert.Equal(HotkeyCaptureAction.Cancel, HotkeyCapture.Interpret(Key.Escape, ModifierKeys.None).Action);
    }

    [Theory]
    [InlineData(Key.Back)]
    [InlineData(Key.Delete)]
    public void Backspace_and_delete_turn_the_shortcut_off(Key key)
    {
        Assert.Equal(HotkeyCaptureAction.Clear, HotkeyCapture.Interpret(key, ModifierKeys.None).Action);
        Assert.Equal(HotkeyCaptureAction.Clear, HotkeyCapture.Interpret(key, ModifierKeys.Shift).Action);
    }

    [Fact]
    public void A_valid_combination_is_accepted()
    {
        var result = HotkeyCapture.Interpret(Key.T, ModifierKeys.Control | ModifierKeys.Alt);

        Assert.Equal(HotkeyCaptureAction.Accept, result.Action);
        Assert.Equal(Hotkey.DefaultSelection, result.Hotkey);
    }

    [Theory]
    [InlineData(Key.T, ModifierKeys.None)]
    [InlineData(Key.T, ModifierKeys.Shift)]
    [InlineData(Key.F5, ModifierKeys.None)]
    public void Combinations_without_ctrl_alt_or_win_are_ignored(Key key, ModifierKeys modifiers)
    {
        Assert.Equal(HotkeyCaptureAction.Ignore, HotkeyCapture.Interpret(key, modifiers).Action);
    }

    [Theory]
    [InlineData(Key.Z, ModifierKeys.Windows | ModifierKeys.Shift)]
    [InlineData(Key.Back, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.Space, ModifierKeys.Control)]
    public void Keys_with_a_real_modifier_become_shortcuts(Key key, ModifierKeys modifiers)
    {
        var result = HotkeyCapture.Interpret(key, modifiers);

        Assert.Equal(HotkeyCaptureAction.Accept, result.Action);
        Assert.Equal(new Hotkey(modifiers, key), result.Hotkey);
    }

    [Fact]
    public void ResolveKey_unwraps_system_ime_and_dead_keys()
    {
        Assert.Equal(Key.T, HotkeyCapture.ResolveKey(Key.System, Key.T, Key.None, Key.None));
        Assert.Equal(Key.A, HotkeyCapture.ResolveKey(Key.ImeProcessed, Key.None, Key.A, Key.None));
        Assert.Equal(Key.OemTilde, HotkeyCapture.ResolveKey(Key.DeadCharProcessed, Key.None, Key.None, Key.OemTilde));
        Assert.Equal(Key.C, HotkeyCapture.ResolveKey(Key.C, Key.None, Key.None, Key.None));
    }
}
