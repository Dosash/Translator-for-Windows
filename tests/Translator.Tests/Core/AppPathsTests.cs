using System.IO;
using Translator.Core;

namespace Translator.Tests.Core;

public class AppPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_override_uses_the_profile_folders(string? value)
    {
        Assert.Null(AppPaths.ResolveOverride(value));
    }

    [Fact]
    public void Relative_override_becomes_a_full_path_without_a_trailing_separator()
    {
        var resolved = AppPaths.ResolveOverride(@"smoke-data\");

        Assert.Equal(Path.Combine(Environment.CurrentDirectory, "smoke-data"), resolved);
    }

    [Fact]
    public void Override_expands_environment_variables_and_strips_quotes()
    {
        var resolved = AppPaths.ResolveOverride("\"%TEMP%\\translator-test\"");

        Assert.Equal(Path.Combine(Path.GetFullPath(Environment.GetEnvironmentVariable("TEMP")!), "translator-test"), resolved);
    }

    [Fact]
    public void Default_profile_keeps_the_plain_app_id()
    {
        Assert.Equal("Translator", AppPaths.InstanceId("Translator", null));
    }

    [Fact]
    public void Data_directory_gets_its_own_instance_id_regardless_of_case()
    {
        var lower = AppPaths.InstanceId("Translator", @"c:\data\one");
        var upper = AppPaths.InstanceId("Translator", @"C:\DATA\ONE");
        var other = AppPaths.InstanceId("Translator", @"C:\data\two");

        Assert.NotEqual("Translator", lower);
        Assert.StartsWith("Translator-", lower);
        Assert.Equal(lower, upper);
        Assert.NotEqual(lower, other);
    }
}
