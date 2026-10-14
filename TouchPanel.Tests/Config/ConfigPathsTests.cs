using System.IO;
using TouchPanel.Config;
using TouchPanel.Services;

namespace TouchPanel.Tests.Config;

public class ConfigPathsTests
{
    [Fact]
    public void Constructor_WithInjectedDirectory_ResolvesAllKnownFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "touchpanel-config-tests", Guid.NewGuid().ToString("N"));

        var paths = new ConfigPaths(directory);

        Assert.Equal(Path.GetFullPath(directory), paths.DataDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(directory), "panel.json"), paths.PanelFile);
        Assert.Equal(Path.Combine(Path.GetFullPath(directory), "panel.backup.json"), paths.BackupFile);
        Assert.Equal(Path.Combine(Path.GetFullPath(directory), "state.json"), paths.StateFile);
    }

    [Fact]
    public void Constructor_WithNullDirectory_UsesRoamingApplicationData()
    {
        var paths = new ConfigPaths();

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppPaths.AppDirectoryName);

        Assert.Equal(expected, paths.DataDirectory);
        Assert.Equal(AppPaths.Default.DataDirectory, paths.DataDirectory);
    }

    [Fact]
    public void Default_UsesConventionalFileNames()
    {
        var paths = ConfigPaths.Default;

        Assert.EndsWith(Path.Combine("TouchPanel", "panel.json"), paths.PanelFile);
        Assert.EndsWith(Path.Combine("TouchPanel", "panel.backup.json"), paths.BackupFile);
        Assert.EndsWith(Path.Combine("TouchPanel", "state.json"), paths.StateFile);
    }

    [Fact]
    public void GetCorruptFilePath_EmbedsTimestampAndFollowsPreservationPattern()
    {
        var directory = Path.Combine(Path.GetTempPath(), "touchpanel-config-tests", Guid.NewGuid().ToString("N"));
        var paths = new ConfigPaths(directory);
        var timestamp = new DateTimeOffset(2026, 10, 8, 14, 30, 12, 345, TimeSpan.FromHours(2));

        var corruptPath = paths.GetCorruptFilePath(timestamp);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(directory), "panel.corrupt-20261008-143012345.json"),
            corruptPath);
        Assert.Equal("panel.corrupt-*.json", ConfigPaths.CorruptFileSearchPattern);
        Assert.StartsWith(ConfigPaths.CorruptFileNamePrefix, Path.GetFileName(corruptPath));
        Assert.EndsWith(".json", corruptPath);
    }

    [Fact]
    public void GetImportBackupFilePath_EmbedsTimestampAndFollowsBackupPattern()
    {
        var directory = Path.Combine(Path.GetTempPath(), "touchpanel-config-tests", Guid.NewGuid().ToString("N"));
        var paths = new ConfigPaths(directory);
        var timestamp = new DateTimeOffset(2026, 10, 9, 8, 5, 1, 7, TimeSpan.FromHours(2));

        var backupPath = paths.GetImportBackupFilePath(timestamp);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(directory), "panel.import-backup-20261009-080501007.json"),
            backupPath);
        Assert.StartsWith(ConfigPaths.ImportBackupFileNamePrefix, Path.GetFileName(backupPath));
        Assert.EndsWith(".json", backupPath);

        // The backup must never be mistaken for the watched effective file (M2-RF-821).
        Assert.NotEqual(ConfigPaths.PanelFileName, Path.GetFileName(backupPath));
    }

    [Fact]
    public void Constructor_ResolvingPaths_DoesNotCreateDirectories()
    {
        var directory = Path.Combine(Path.GetTempPath(), "touchpanel-config-tests-unused-" + Guid.NewGuid().ToString("N"));

        _ = new ConfigPaths(directory);

        Assert.False(Directory.Exists(directory));
    }
}
