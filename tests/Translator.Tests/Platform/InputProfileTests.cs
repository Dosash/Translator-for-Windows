using Translator.Platform;

namespace Translator.Tests.Platform;

public class InputProfileTests
{
    [Theory]
    [InlineData("WindowsTerminal.exe", null)]
    [InlineData(null, "CASCADIA_HOSTING_WINDOW_CLASS")]
    [InlineData(null, "ConsoleWindowClass")]
    [InlineData("mintty.exe", null)]
    [InlineData(null, "mintty")]
    [InlineData("ConEmu64.exe", null)]
    [InlineData("ConEmuC.exe", null)]
    [InlineData("Cmder.exe", null)]
    [InlineData(null, "VirtualConsoleClass")]
    [InlineData("alacritty.exe", null)]
    [InlineData("wezterm-gui.exe", null)]
    [InlineData("Tabby.exe", null)]
    [InlineData("Hyper.exe", null)]
    [InlineData("WindTerm.exe", null)]
    [InlineData("FluentTerminal.exe", null)]
    [InlineData("MobaXterm.exe", null)]
    [InlineData("Code.exe", null)]
    [InlineData("Cursor.exe", null)]
    [InlineData("Windsurf.exe", null)]
    [InlineData("devenv.exe", null)]
    [InlineData("Zed.exe", null)]
    [InlineData("Fleet.exe", null)]
    [InlineData("idea64.exe", null)]
    [InlineData("pycharm64.exe", null)]
    [InlineData("rider64.exe", null)]
    [InlineData("webstorm64.exe", null)]
    [InlineData("clion64.exe", null)]
    [InlineData("goland64.exe", null)]
    [InlineData("phpstorm64.exe", null)]
    [InlineData("rustrover64.exe", null)]
    [InlineData("datagrip64.exe", null)]
    [InlineData("studio64.exe", null)]
    public void Classify_returns_InsertChord_for_terminals_and_terminal_hosting_editors(string? processName, string? className)
    {
        Assert.Equal(CopyPasteChord.InsertChord, InputProfile.Classify(processName, className));
    }

    [Theory]
    [InlineData("putty.exe", null)]
    [InlineData("kitty.exe", null)]
    [InlineData(null, "PuTTY")]
    [InlineData(null, "KiTTY")]
    public void Classify_returns_CopyOnSelect_for_putty_family(string? processName, string? className)
    {
        Assert.Equal(CopyPasteChord.CopyOnSelect, InputProfile.Classify(processName, className));
    }

    [Theory]
    [InlineData("notepad.exe", null)]
    [InlineData("chrome.exe", "Chrome_WidgetWin_1")]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("some-unknown-app.exe", "SomeRandomWindowClass")]
    public void Classify_defaults_to_CtrlCV_for_everything_else(string? processName, string? className)
    {
        Assert.Equal(CopyPasteChord.CtrlCV, InputProfile.Classify(processName, className));
    }

    [Theory]
    [InlineData("WINDOWSTERMINAL.EXE")]
    [InlineData("windowsterminal.exe")]
    [InlineData("WindowsTerminal.EXE")]
    public void Classify_matches_process_name_case_insensitively(string processName)
    {
        Assert.Equal(CopyPasteChord.InsertChord, InputProfile.Classify(processName, null));
    }

    [Theory]
    [InlineData("cascadia_hosting_window_class")]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS")]
    public void Classify_matches_class_name_case_insensitively(string className)
    {
        Assert.Equal(CopyPasteChord.InsertChord, InputProfile.Classify(null, className));
    }

    [Fact]
    public void Classify_prefix_wildcard_does_not_match_unrelated_names()
    {
        // "conemu*" must not swallow apps that merely contain "conemu" mid-name or share a suffix.
        Assert.Equal(CopyPasteChord.CtrlCV, InputProfile.Classify("notconemu.exe", null));
        Assert.Equal(CopyPasteChord.CtrlCV, InputProfile.Classify("myconemuwrapper", null));
    }

    [Fact]
    public void ForWindow_falls_back_to_CtrlCV_for_a_null_window_handle()
    {
        Assert.Equal(CopyPasteChord.CtrlCV, InputProfile.ForWindow(IntPtr.Zero));
    }
}
