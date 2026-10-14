using System.IO;
using TouchPanel.Services;

namespace TouchPanel.Tests.Services;

public class AppPathsTests
{
    [Fact]
    public void Constructor_WithInjectedRoots_ResolvesLogsAndDataUnderThoseRoots()
    {
        var localRoot = Path.Combine(Path.GetTempPath(), "tp-tests-local");
        var roamingRoot = Path.Combine(Path.GetTempPath(), "tp-tests-roaming");

        var paths = new AppPaths(localRoot, roamingRoot);

        Assert.Equal(Path.Combine(localRoot, "TouchPanel", "logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(roamingRoot, "TouchPanel"), paths.DataDirectory);
    }

    [Fact]
    public void Constructor_WithNullRoots_UsesUserProfileFolders()
    {
        var paths = new AppPaths();

        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            paths.LogsDirectory);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            paths.DataDirectory);
    }

    [Fact]
    public void Default_UsesConventionalFolderNames()
    {
        var paths = AppPaths.Default;

        Assert.EndsWith(Path.Combine("TouchPanel", "logs"), paths.LogsDirectory);
        Assert.EndsWith("TouchPanel", paths.DataDirectory);
    }

    [Fact]
    public void Constructor_ResolvingPaths_DoesNotCreateDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "tp-tests-unused-" + Guid.NewGuid().ToString("N"));

        _ = new AppPaths(root, root);

        Assert.False(Directory.Exists(root));
    }
}
