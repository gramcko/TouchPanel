using System.IO;
using System.Text.Json;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Backup rotation and recovery semantics (RF-078): every save from a known-good file rotates the
/// previous version into <c>panel.backup.json</c>; corrupt/future sources are preserved but never
/// become the backup; recovery falls back from backup to generated defaults.
/// </summary>
public class BackupRecoveryTests
{
    [Fact]
    public async Task Save_RotatesThePreviousValidatedFileIntoTheBackup()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);

        var first = DefaultConfigGenerator.Create();
        first.Window.AlwaysOnTop = true;
        Assert.True((await store.SaveAsync(first)).Succeeded);

        var second = DefaultConfigGenerator.Create();
        second.Window.AlwaysOnTop = false;
        Assert.True((await store.SaveAsync(second)).Succeeded);

        Assert.True(File.Exists(temp.Config.BackupFile));
        var backup = Deserialize(File.ReadAllText(temp.Config.BackupFile));
        Assert.True(backup.Window.AlwaysOnTop);
        var panel = Deserialize(File.ReadAllText(temp.Config.PanelFile));
        Assert.False(panel.Window.AlwaysOnTop);
    }

    [Fact]
    public async Task CorruptPanel_RecoversFromBackupWhenAvailable()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);

        var first = DefaultConfigGenerator.Create();
        first.Window.AlwaysOnTop = true;
        Assert.True((await store.SaveAsync(first)).Succeeded);
        Assert.True((await store.SaveAsync(DefaultConfigGenerator.Create())).Succeeded); // rotates `first` into the backup

        File.WriteAllText(temp.Config.PanelFile, "{ corrupt");

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Invalid, result.State);
        Assert.True(result.Config!.Window.AlwaysOnTop);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.recovered.backup");
    }

    [Fact]
    public async Task SaveAfterCorruptLoad_DoesNotRotateTheCorruptSourceIntoTheBackup()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);

        var knownGood = DefaultConfigGenerator.Create();
        knownGood.Window.AlwaysOnTop = true;
        Assert.True((await store.SaveAsync(knownGood)).Succeeded);
        Assert.True((await store.SaveAsync(DefaultConfigGenerator.Create())).Succeeded); // backup := knownGood
        var backupBytes = File.ReadAllBytes(temp.Config.BackupFile);

        File.WriteAllText(temp.Config.PanelFile, "{ corrupt");
        var load = await store.LoadAsync();
        Assert.Equal(ConfigState.Invalid, load.State);
        Assert.True(load.Config!.Window.AlwaysOnTop);

        var save = await store.SaveAsync(load.Config);

        Assert.True(save.Succeeded);
        // The invalid source must never become the last known valid backup.
        Assert.Equal(backupBytes, File.ReadAllBytes(temp.Config.BackupFile));
        Assert.True(Deserialize(File.ReadAllText(temp.Config.BackupFile)).Window.AlwaysOnTop);
        Assert.True(Deserialize(File.ReadAllText(temp.Config.PanelFile)).Window.AlwaysOnTop);
    }

    [Fact]
    public async Task CorruptPanel_WithoutBackup_RecoversDefaults()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        File.WriteAllText(temp.Config.PanelFile, "{ corrupt");

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Invalid, result.State);
        Assert.NotNull(result.Config);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.recovered.defaults");
    }

    [Fact]
    public async Task CorruptBackup_IsReportedAndDefaultsAreUsed()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        File.WriteAllText(temp.Config.PanelFile, "{ corrupt");
        File.WriteAllText(temp.Config.BackupFile, "{ also corrupt");

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Invalid, result.State);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.backup.invalid");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.recovered.defaults");
    }

    [Fact]
    public async Task FutureBackup_IsIgnoredAndDefaultsAreUsed()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        File.WriteAllText(temp.Config.PanelFile, "{ corrupt");
        File.WriteAllText(temp.Config.BackupFile, """{ "schemaVersion": 99 }""");

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Invalid, result.State);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.backup.invalid");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.recovered.defaults");
    }

    [Fact]
    public async Task RepeatedCorruptLoads_PreserveEachSourceWithoutOverwritingEarlierCopies()
    {
        using var temp = new TempDirectory();
        var time = new FakeTimeProvider();
        const string garbage = "{ corrupt";
        File.WriteAllText(temp.Config.PanelFile, garbage);

        var firstStore = CreateStore(temp, time);
        Assert.Equal(ConfigState.Invalid, (await firstStore.LoadAsync()).State);
        var secondStore = CreateStore(temp, time);
        Assert.Equal(ConfigState.Invalid, (await secondStore.LoadAsync()).State);

        var preserved = Directory.GetFiles(temp.DirectoryPath, ConfigPaths.CorruptFileSearchPattern);
        Assert.Equal(2, preserved.Length);
        Assert.All(preserved, file => Assert.Equal(System.Text.Encoding.UTF8.GetBytes(garbage), File.ReadAllBytes(file)));
    }

    [Fact]
    public async Task PreservedSources_SurviveLaterSaves()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        File.WriteAllText(temp.Config.PanelFile, "{ corrupt");

        var load = await store.LoadAsync();
        var preserved = Assert.Single(Directory.GetFiles(temp.DirectoryPath, ConfigPaths.CorruptFileSearchPattern));

        Assert.True((await store.SaveAsync(load.Config!)).Succeeded);

        Assert.True(File.Exists(preserved));
    }

    private static JsonConfigStore CreateStore(TempDirectory temp, TimeProvider? timeProvider = null)
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new RecordingSink())
            .CreateLogger();

        return new JsonConfigStore(temp.Config, migrations: null, timeProvider: timeProvider, logger: logger);
    }

    private static PanelConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
}
