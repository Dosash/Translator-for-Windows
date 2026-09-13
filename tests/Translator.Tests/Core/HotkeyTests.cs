using System.Windows.Input;
using Translator.Core;

namespace Translator.Tests.Core;

public class HotkeyTests
{
    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, Key.T, "Ctrl+Alt+T")]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, Key.Space, "Ctrl+Alt+Space")]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.C, "Ctrl+Alt+Shift+C")]
    [InlineData(ModifierKeys.Windows, Key.D5, "Win+5")]
    [InlineData(ModifierKeys.Control, Key.OemComma, "Ctrl+,")]
    public void DisplayThenParseRoundTrips(ModifierKeys modifiers, Key key, string expectedDisplay)
    {
        var hotkey = new Hotkey(modifiers, key);
        Assert.Equal(expectedDisplay, hotkey.Display);

        Assert.True(Hotkey.TryParse(hotkey.Display, out var parsed));
        Assert.Equal(hotkey, parsed);
    }

    [Fact]
    public void NoneHasEmptyDisplayAndDoesNotParse()
    {
        Assert.Equal(string.Empty, Hotkey.None.Display);
        Assert.True(Hotkey.None.IsNone);
        Assert.False(Hotkey.TryParse(string.Empty, out var parsed));
        Assert.Equal(Hotkey.None, parsed);
        Assert.False(Hotkey.TryParse(null, out _));
    }

    [Fact]
    public void IsValidRequiresCtrlAltOrWindows()
    {
        Assert.True(new Hotkey(ModifierKeys.Control, Key.T).IsValid);
        Assert.True(new Hotkey(ModifierKeys.Alt, Key.T).IsValid);
        Assert.True(new Hotkey(ModifierKeys.Windows, Key.T).IsValid);
        Assert.False(new Hotkey(ModifierKeys.Shift, Key.T).IsValid);
        Assert.False(new Hotkey(ModifierKeys.None, Key.T).IsValid);
        Assert.False(Hotkey.None.IsValid);
    }

    [Fact]
    public void TryParseFailsForUnrecognizedToken()
    {
        Assert.False(Hotkey.TryParse("Ctrl+Alt+NotAKey", out _));
    }

    [Fact]
    public void TryParseFailsWithTwoNonModifierTokens()
    {
        Assert.False(Hotkey.TryParse("Ctrl+T+C", out _));
    }

    [Fact]
    public void DefaultHotkeysAreValid()
    {
        Assert.True(Hotkey.DefaultSelection.IsValid);
        Assert.True(Hotkey.DefaultClipboard.IsValid);
        Assert.True(Hotkey.DefaultPanel.IsValid);
    }
}
