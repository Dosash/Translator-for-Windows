using Translator.UI.Shell;

namespace Translator.Tests.UI;

public class CommandLineTests
{
    [Fact]
    public void No_arguments_is_a_normal_start()
    {
        Assert.Equal(CliCommandKind.Normal, CommandLine.Parse([]).Kind);
    }

    [Fact]
    public void Autostart_flag()
    {
        Assert.Equal(CliCommandKind.Autostart, CommandLine.Parse(["--autostart"]).Kind);
    }

    [Fact]
    public void Translate_takes_the_next_argument_as_text()
    {
        var command = CommandLine.Parse(["--translate", "hello world"]);

        Assert.Equal(CliCommandKind.Translate, command.Kind);
        Assert.Equal("hello world", command.Text);
    }

    [Fact]
    public void Flags_are_case_insensitive()
    {
        Assert.Equal(CliCommandKind.Translate, CommandLine.Parse(["--TRANSLATE", "x"]).Kind);
    }

    [Theory]
    [InlineData("--translate")]
    [InlineData("--test-translate")]
    public void Missing_text_is_invalid(string flag)
    {
        var command = CommandLine.Parse([flag]);

        Assert.Equal(CliCommandKind.Invalid, command.Kind);
        Assert.False(string.IsNullOrEmpty(command.Error));
    }

    [Fact]
    public void TestTranslate_with_text()
    {
        var command = CommandLine.Parse(["--test-translate", "привет"]);

        Assert.Equal(CliCommandKind.TestTranslate, command.Kind);
        Assert.Equal("привет", command.Text);
    }

    [Fact]
    public void TestOffline_with_target_only_detects_the_source()
    {
        var command = CommandLine.Parse(["--test-offline", "hello", "ru"]);

        Assert.Equal(CliCommandKind.TestOffline, command.Kind);
        Assert.Equal("hello", command.Text);
        Assert.Null(command.Source);
        Assert.Equal("ru", command.Target);
    }

    [Fact]
    public void TestOffline_with_source_and_target()
    {
        var command = CommandLine.Parse(["--test-offline", "hello", "en", "de"]);

        Assert.Equal("en", command.Source);
        Assert.Equal("de", command.Target);
    }

    [Fact]
    public void TestOffline_auto_source_means_detect()
    {
        Assert.Null(CommandLine.Parse(["--test-offline", "hello", "auto", "de"]).Source);
    }

    [Fact]
    public void TestOffline_without_target_is_invalid()
    {
        Assert.Equal(CliCommandKind.Invalid, CommandLine.Parse(["--test-offline", "hello"]).Kind);
    }

    [Theory]
    [InlineData("panel", AppWindowKind.Panel)]
    [InlineData("settings", AppWindowKind.Settings)]
    [InlineData("history", AppWindowKind.History)]
    [InlineData("offline", AppWindowKind.Offline)]
    [InlineData("firstrun", AppWindowKind.FirstRun)]
    [InlineData("Settings", AppWindowKind.Settings)]
    public void Open_names_a_window(string name, AppWindowKind window)
    {
        var command = CommandLine.Parse(["--open", name]);

        Assert.Equal(CliCommandKind.Open, command.Kind);
        Assert.Equal(window, command.Window);
    }

    [Theory]
    [InlineData]
    [InlineData("bubble")]
    [InlineData("")]
    public void Open_without_a_known_window_is_invalid(params string[] rest)
    {
        var command = CommandLine.Parse(["--open", .. rest]);

        Assert.Equal(CliCommandKind.Invalid, command.Kind);
        Assert.Contains("firstrun", command.Error);
    }

    [Theory]
    [InlineData("--translate-screen")]
    [InlineData("--Translate-Screen")]
    public void TranslateScreen_flag_needs_no_text(string flag)
    {
        var command = CommandLine.Parse([flag]);

        Assert.Equal(CliCommandKind.TranslateScreen, command.Kind);
        Assert.Null(command.Text);
    }

    [Fact]
    public void TranslateScreen_is_not_mistaken_for_translate_with_text()
    {
        Assert.Equal(CliCommandKind.TranslateScreen, CommandLine.Parse(["--translate-screen", "ignored"]).Kind);
        Assert.Equal(CliCommandKind.Translate, CommandLine.Parse(["--translate", "--translate-screen"]).Kind);
    }

    [Fact]
    public void Quit_flag()
    {
        Assert.Equal(CliCommandKind.Quit, CommandLine.Parse(["--QUIT"]).Kind);
    }

    [Theory]
    [InlineData("Привет, мир", "ru", "en")]
    [InlineData("Hello world", "en", "ru")]
    [InlineData("Добрий день", "ru", "en")]
    public void DetectDirection_matches_macOS(string text, string source, string target)
    {
        Assert.Equal((source, target), CommandLine.DetectDirection(text));
    }
}
