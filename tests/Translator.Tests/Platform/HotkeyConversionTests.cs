using System.Windows.Input;
using Translator.Core;
using Translator.Platform;

namespace Translator.Tests.Platform;

public class HotkeyConversionTests
{
    [Fact]
    public void Converts_modifiers_and_key_to_win32_form()
    {
        var hotkey = new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.T);
        var ok = HotkeyManager.TryConvertToWin32(hotkey, out var modifiers, out var vk);

        Assert.True(ok);
        Assert.Equal(0x02u | 0x01u, modifiers); // MOD_CONTROL | MOD_ALT
        Assert.Equal((uint)KeyInterop.VirtualKeyFromKey(Key.T), vk);
    }

    [Fact]
    public void Includes_shift_and_win_bits()
    {
        var hotkey = new Hotkey(ModifierKeys.Shift | ModifierKeys.Windows | ModifierKeys.Control, Key.Space);
        var ok = HotkeyManager.TryConvertToWin32(hotkey, out var modifiers, out _);

        Assert.True(ok);
        Assert.Equal(0x02u | 0x04u | 0x08u, modifiers); // MOD_CONTROL | MOD_SHIFT | MOD_WIN
    }

    [Fact]
    public void None_is_invalid_for_conversion()
    {
        var ok = HotkeyManager.TryConvertToWin32(Hotkey.None, out var modifiers, out var vk);

        Assert.False(ok);
        Assert.Equal(0u, modifiers);
        Assert.Equal(0u, vk);
    }

    [Fact]
    public void Plain_key_without_ctrl_alt_or_win_is_invalid()
    {
        // Shift-only would capture plain typing — Hotkey.IsValid (and thus conversion) rejects it.
        var hotkey = new Hotkey(ModifierKeys.Shift, Key.T);
        Assert.False(HotkeyManager.TryConvertToWin32(hotkey, out _, out _));
    }
}
