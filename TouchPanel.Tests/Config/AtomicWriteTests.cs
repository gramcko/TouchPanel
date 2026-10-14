using System.IO;
using System.Text.Json;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Atomic-save behavior (RF-077): temp file + flush + replace, the <c>File.Move</c> fallback when
/// no destination exists, retryability after a failure, and—critically—the guarantee that a
/// simulated interruption between the temp write and the replace never leaves a partial
/// <c>panel.json</c> or destroys the previous file.
/// </summary>
public class AtomicWriteTests
{
    [Fact]
    public async Task SaveAsync_WritesTheFileAndLeavesNoTempFileBehind()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        var config = DefaultConfigGenerator.Create();
        config.Window.AlwaysOnTop = true;

        var result = await store.SaveAsync(config);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.True(File.Exists(temp.Config.PanelFile));
        Assert.False(File.Exists(temp.Config.PanelFile + ".tmp"));

        var persisted = Deserialize(File.ReadAllText(temp.Config.PanelFile));
        Assert.True(persisted.Window.AlwaysOnTop);
    }

    [Fact]
    public async Task SaveAsync_WithNoDestination_UsesTheMoveFallback()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);

        var result = await store.SaveAsync(DefaultConfigGenerator.Create());

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(temp.Config.PanelFile));
        Assert.False(File.Exists(temp.Config.PanelFile + ".tmp"));
        Assert.False(File.Exists(temp.Config.BackupFile));
    }

    [Fact]
    public async Task SaveAsync_InterruptedBetweenTempWriteAndReplace_LeavesThePreviousFileIntact()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);

        // Seed a known-good panel.json (this is the "previous successfully validated version").
        var original = DefaultConfigGenerator.Create();
        original.Window.AlwaysOnTop = false;
        Assert.True((await store.SaveAsync(original)).Succeeded);
        var originalBytes = File.ReadAllBytes(temp.Config.PanelFile);

        // Simulate the interruption: the destination is locked, so the replace step fails after
        // the temp file was written and flushed.
        var updated = DefaultConfigGenerator.Create();
        updated.Window.AlwaysOnTop = true;

        ConfigSaveResult failed;
        using (new FileStream(temp.Config.PanelFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            failed = await store.SaveAsync(updated);
        }

        Assert.False(failed.Succeeded);
        Assert.Contains(failed.Diagnostics, diagnostic => diagnostic.Code == "config.save.failed");
        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.PanelFile));
        Assert.False(File.Exists(temp.Config.PanelFile + ".tmp"));

        // RF-077: the failure is retryable on the next save and then rotates the old file.
        var retry = await store.SaveAsync(updated);

        Assert.True(retry.Succeeded);
        Assert.True(Deserialize(File.ReadAllText(temp.Config.PanelFile)).Window.AlwaysOnTop);
        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.BackupFile));
    }

    [Fact]
    public async Task SaveAsync_WhenTheBackupIsLocked_LeavesThePreviousFileIntact()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);

        var original = DefaultConfigGenerator.Create();
        original.Window.AlwaysOnTop = false;
        Assert.True((await store.SaveAsync(original)).Succeeded);
        Assert.True((await store.SaveAsync(DefaultConfigGenerator.Create())).Succeeded); // creates the backup
        var panelBytes = File.ReadAllBytes(temp.Config.PanelFile);

        var updated = DefaultConfigGenerator.Create();
        updated.Window.AlwaysOnTop = true;

        ConfigSaveResult failed;
        using (new FileStream(temp.Config.BackupFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            failed = await store.SaveAsync(updated);
        }

        Assert.False(failed.Succeeded);
        Assert.Equal(panelBytes, File.ReadAllBytes(temp.Config.PanelFile));
        Assert.False(File.Exists(temp.Config.PanelFile + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_ConcurrentSaves_AreSerializedAndTheFileStaysValid()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);

        var first = DefaultConfigGenerator.Create();
        first.Window.AlwaysOnTop = true;
        var second = DefaultConfigGenerator.Create();
        second.Window.AlwaysOnTop = false;

        var results = await Task.WhenAll(store.SaveAsync(first).AsTask(), store.SaveAsync(second).AsTask());

        Assert.All(results, result => Assert.True(result.Succeeded));
        var persisted = Deserialize(File.ReadAllText(temp.Config.PanelFile));
        Assert.Equal(PanelConfig.CurrentSchemaVersion, persisted.SchemaVersion);
        Assert.False(File.Exists(temp.Config.PanelFile + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_RefusesToWriteASchemaVersionThisBuildDoesNotOwn()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        var future = DefaultConfigGenerator.Create();
        future.SchemaVersion = PanelConfig.CurrentSchemaVersion + 1;

        var result = await store.SaveAsync(future);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.save.version");
        Assert.False(File.Exists(temp.Config.PanelFile));
    }

    [Fact]
    public async Task SaveAsync_CanceledBeforeWrite_LeavesEverythingUnchanged()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        Assert.True((await store.SaveAsync(DefaultConfigGenerator.Create())).Succeeded);
        var originalBytes = File.ReadAllBytes(temp.Config.PanelFile);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await store.SaveAsync(DefaultConfigGenerator.Create(), cts.Token);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.save.canceled");
        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.PanelFile));
        Assert.False(File.Exists(temp.Config.PanelFile + ".tmp"));
    }

    private static JsonConfigStore CreateStore(TempDirectory temp)
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new RecordingSink())
            .CreateLogger();

        return new JsonConfigStore(temp.Config, migrations: null, timeProvider: null, logger: logger);
    }

    private static PanelConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
}
