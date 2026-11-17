using TouchPanel.Services.Updates;

namespace TouchPanel.Tests.Services;

/// <summary>
/// Release-tag parsing and comparison used by the manual update check: optional <c>v</c> prefix,
/// one to three numeric components, build metadata ignored, semver pre-release precedence, and
/// malformed tags rejected without throwing.
/// </summary>
public sealed class VersionTagTests
{
    [Theory]
    [InlineData("v1.0.2", 1, 0, 2)]
    [InlineData("V1.0.2", 1, 0, 2)]
    [InlineData("1.0.2", 1, 0, 2)]
    [InlineData("v2.5", 2, 5, 0)]
    [InlineData("3", 3, 0, 0)]
    [InlineData("  v1.2.3  ", 1, 2, 3)]
    [InlineData("v1.2.3+build.7", 1, 2, 3)]
    public void TryParse_AcceptsTagShapes(string tag, int major, int minor, int patch)
    {
        Assert.True(VersionTag.TryParse(tag, out var version));

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Null(version.PreRelease);
    }

    [Fact]
    public void TryParse_KeepsPreReleaseLabelAndNormalizesTheText()
    {
        Assert.True(VersionTag.TryParse("v1.0.2-beta.1", out var version));

        Assert.Equal("beta.1", version.PreRelease);
        Assert.Equal("1.0.2-beta.1", version.ToString());
        Assert.Equal("1.0.2", new VersionTag(1, 0, 2).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("V")]
    [InlineData("latest")]
    [InlineData("v1.2.3.4")]
    [InlineData("1..2")]
    [InlineData("v1.0.x")]
    [InlineData("1.-2")]
    [InlineData("v1.0.2-")]
    public void TryParse_RejectsMalformedOrMissingTags(string? tag)
    {
        Assert.False(VersionTag.TryParse(tag, out _));
        Assert.Null(VersionTag.ParseOrNull(tag));
    }

    [Theory]
    [InlineData("v1.0.2", "v1.0.3", -1)]
    [InlineData("v1.0.3", "v1.0.2", 1)]
    [InlineData("1.0.2", "v1.0.2", 0)]
    [InlineData("v1.2", "v1.2.0", 0)]
    [InlineData("v2.0.0", "v1.99.99", 1)]
    [InlineData("v1.0.10", "v1.0.9", 1)]
    [InlineData("v1.0.2-beta", "v1.0.2", -1)]
    [InlineData("v1.0.2", "v1.0.2-beta", 1)]
    public void CompareTo_OrdersVersions(string leftTag, string rightTag, int expectedSign)
    {
        var left = Assert.IsType<VersionTag>(VersionTag.ParseOrNull(leftTag));
        var right = Assert.IsType<VersionTag>(VersionTag.ParseOrNull(rightTag));

        Assert.Equal(expectedSign, Math.Sign(left.CompareTo(right)));
        Assert.Equal(-expectedSign, Math.Sign(right.CompareTo(left)));
    }
}
