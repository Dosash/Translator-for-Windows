using Translator.Core;

namespace Translator.Tests.Core;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3+abcdef", "1.2.3")]
    [InlineData("V2.0", "2.0")]
    [InlineData(" v1.0.0 ", "1.0.0")]
    public void NormalizeVersion_StripsVPrefixAndMetadata(string raw, string expected)
    {
        Assert.Equal(expected, UpdateChecker.NormalizeVersion(raw));
    }

    [Theory]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("1.1.9", "1.2.0", false)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.10", "1.0.9", true)]
    [InlineData("1.0", "1.0.0", false)]
    [InlineData("1.0.1", "1.0", true)]
    public void IsNewer_ComparesNumerically(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(latest, current));
    }

    [Fact]
    public void CompareVersions_HandlesVPrefixAndMetadata()
    {
        Assert.True(UpdateChecker.CompareVersions("v1.3.0", "1.2.9+build5") > 0);
        Assert.Equal(0, UpdateChecker.CompareVersions("v1.0.0", "1.0.0+meta"));
    }
}
