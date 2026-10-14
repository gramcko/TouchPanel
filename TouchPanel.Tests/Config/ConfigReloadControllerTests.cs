using System.IO;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Temp-directory integration checks for the hot-reload watcher adapter (T-11 part 1,
/// M2-RF-821/M2-RF-823/M2-RF-827/M2-RF-828): a <see cref="FileSystemWatcher"/> scoped to
/// <c>panel.json</c> raises a debounced reload request only for that file, the app stores' own
/// saves are suppressed, and disposal stops all watching. These are the only tests that touch the
/// real file system; the decision semantics are covered deterministically elsewhere.
/// </summary>
public sealed class ConfigReloadControllerTests
{
    [Fact]
    public async Task Watcher_IsFilteredToPanelJson_AndRaisesADebouncedRequest()
    {
        using var temp = new TempDirectory();
        using var engine = new ReloadDecisionEngine(TimeProvider.System, TimeSpan.FromMilliseconds(80), TimeSpan.Zero);
        using var controller = new ConfigReloadController(engine, temp.Config, TimeProvider.System, logger: null);

        var reloads = 0;
        controller.ReloadRequested += (_, _) => Interlocked.Increment(ref reloads);

        controller.Start();
        Assert.True(controller.IsRunning);
        await Task.Delay(150);

        // Every sibling file is invisible to the watcher (M2-RF-821).
        await File.WriteAllTextAsync(temp.Config.StateFile, "{}");
        await File.WriteAllTextAsync(temp.Config.BackupFile, "{}");
        await File.WriteAllTextAsync(temp.Config.RecoveryFile, "{}");
        await File.WriteAllTextAsync(temp.Config.PanelFile + ".tmp", "{}");
        await File.WriteAllTextAsync(
            Path.Combine(temp.Config.DataDirectory, "panel.corrupt-20261009-120000000.json"),
            "{}");

        await Task.Delay(400);
        Assert.Equal(0, Volatile.Read(ref reloads));

        // The effective file does produce exactly one coalesced reload.
        await File.WriteAllTextAsync(temp.Config.PanelFile, """{"schemaVersion":2}""");
        Assert.True(await WaitForAsync(() => Volatile.Read(ref reloads) >= 1, TimeSpan.FromSeconds(5)));
        await Task.Delay(250);
        Assert.Equal(1, Volatile.Read(ref reloads));
    }

    [Fact]
    public async Task AppStoreSaves_DoNotRaiseAReloadRequest()
    {
        using var temp = new TempDirectory();
        using var engine = new ReloadDecisionEngine(TimeProvider.System, TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(700));
        using var controller = new ConfigReloadController(engine, temp.Config, TimeProvider.System, logger: null);

        var reloads = 0;
        controller.ReloadRequested += (_, _) => Interlocked.Increment(ref reloads);

        controller.Start();
        await Task.Delay(150);

        var configStore = new JsonConfigStore(temp.Config, logger: null, writeSuppression: engine);
        Assert.True((await configStore.SaveAsync(DefaultConfigGenerator.Create())).Succeeded);

        var stateStore = new StateStore(temp.Config, logger: null, writeSuppression: engine);
        Assert.True(await stateStore.SaveAsync(new RuntimeState { CloseNotificationShown = true }));

        // The watcher event for the config save arrives inside the self-write window and is ignored.
        await Task.Delay(1000);
        Assert.Equal(0, Volatile.Read(ref reloads));
    }

    [Fact]
    public async Task Dispose_StopsWatching_AndIsIdempotent()
    {
        using var temp = new TempDirectory();
        using var engine = new ReloadDecisionEngine(TimeProvider.System, TimeSpan.FromMilliseconds(80), TimeSpan.Zero);
        var controller = new ConfigReloadController(engine, temp.Config, TimeProvider.System, logger: null);

        var reloads = 0;
        controller.ReloadRequested += (_, _) => Interlocked.Increment(ref reloads);

        controller.Start();
        await Task.Delay(150);

        controller.Dispose();
        controller.Dispose();

        Assert.False(controller.IsRunning);

        await File.WriteAllTextAsync(temp.Config.PanelFile, "{}");
        await Task.Delay(400);

        Assert.Equal(0, Volatile.Read(ref reloads));
    }

    [Fact]
    public async Task Start_WhenTheDirectoryCannotBePrepared_DegradesWithoutThrowing_ThenRecovers()
    {
        using var temp = new TempDirectory();
        var blockedPath = Path.Combine(temp.DirectoryPath, "blocked");
        await File.WriteAllTextAsync(blockedPath, "not a directory");

        using var engine = new ReloadDecisionEngine(TimeProvider.System, TimeSpan.FromMilliseconds(80), TimeSpan.Zero);
        using var controller = new ConfigReloadController(engine, new ConfigPaths(blockedPath), TimeProvider.System, logger: null);

        controller.Start();
        Assert.False(controller.IsRunning);

        // Removing the conflict lets a later Start re-establish the watcher.
        File.Delete(blockedPath);
        controller.Start();
        Assert.True(controller.IsRunning);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }
}
