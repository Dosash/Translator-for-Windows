using Microsoft.Win32;
using Translator.Platform;

namespace Translator.Tests.Platform;

/// <summary>Exercises Autostart's internal (path-parameterized) overloads against a throwaway HKCU subkey — never the real Run key.</summary>
public class AutostartTests : IDisposable
{
    private const string ValueName = "TranslatorTest";
    private readonly string _root = $@"Software\TranslatorTests\{Guid.NewGuid():N}";
    private readonly string _runKeyPath;
    private readonly string _approvedKeyPath;
    private const string ExePath = @"C:\Fake\Translator.exe";

    public AutostartTests()
    {
        _runKeyPath = $@"{_root}\Run";
        _approvedKeyPath = $@"{_root}\StartupApproved\Run";
        Registry.CurrentUser.CreateSubKey(_runKeyPath)!.Dispose();
        Registry.CurrentUser.CreateSubKey(_approvedKeyPath)!.Dispose();
    }

    [Fact]
    public void Disabled_by_default()
    {
        Assert.False(Autostart.IsEnabled(_runKeyPath, _approvedKeyPath, ValueName, ExePath));
    }

    [Fact]
    public void SetEnabled_true_then_IsEnabled_true()
    {
        Autostart.SetEnabled(true, _runKeyPath, _approvedKeyPath, ValueName, ExePath);
        Assert.True(Autostart.IsEnabled(_runKeyPath, _approvedKeyPath, ValueName, ExePath));

        using var runKey = Registry.CurrentUser.OpenSubKey(_runKeyPath);
        var command = Assert.IsType<string>(runKey!.GetValue(ValueName));
        Assert.Contains(ExePath, command);
        Assert.Contains(Autostart.LaunchArgument, command);
    }

    [Fact]
    public void SetEnabled_false_removes_the_value()
    {
        Autostart.SetEnabled(true, _runKeyPath, _approvedKeyPath, ValueName, ExePath);
        Autostart.SetEnabled(false, _runKeyPath, _approvedKeyPath, ValueName, ExePath);

        using var runKey = Registry.CurrentUser.OpenSubKey(_runKeyPath);
        Assert.Null(runKey!.GetValue(ValueName));
        Assert.False(Autostart.IsEnabled(_runKeyPath, _approvedKeyPath, ValueName, ExePath));
    }

    [Fact]
    public void Disabled_in_StartupApproved_overrides_the_Run_value()
    {
        Autostart.SetEnabled(true, _runKeyPath, _approvedKeyPath, ValueName, ExePath);
        using (var approvedKey = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: true))
        {
            approvedKey!.SetValue(ValueName, new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        Assert.False(Autostart.IsEnabled(_runKeyPath, _approvedKeyPath, ValueName, ExePath));
    }

    [Fact]
    public void SetEnabled_true_clears_a_prior_StartupApproved_disable()
    {
        using (var approvedKey = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: true))
        {
            approvedKey!.SetValue(ValueName, new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        Autostart.SetEnabled(true, _runKeyPath, _approvedKeyPath, ValueName, ExePath);

        Assert.True(Autostart.IsEnabled(_runKeyPath, _approvedKeyPath, ValueName, ExePath));
    }

    [Fact]
    public void A_value_pointing_at_a_different_exe_is_not_reported_as_enabled()
    {
        using (var runKey = Registry.CurrentUser.CreateSubKey(_runKeyPath))
        {
            runKey!.SetValue(ValueName, @"""C:\Other\App.exe"" --autostart");
        }

        Assert.False(Autostart.IsEnabled(_runKeyPath, _approvedKeyPath, ValueName, ExePath));
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
    }
}
