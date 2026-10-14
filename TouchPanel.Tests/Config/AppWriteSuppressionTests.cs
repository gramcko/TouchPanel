using System.IO;
using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// App-originated write suppression seam (T-11 part 1, M2-RF-823): both the config store and the
/// runtime state store open a self-write window on the shared decision engine, and the config store
/// contributes the exact content it wrote so an identical candidate is a no-op. This is verified
/// deterministically with an injected fake clock and no watcher.
/// </summary>
public sealed class AppWriteSuppressionTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan SelfWrite = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task JsonConfigStore_SaveAsync_SuppressesTheFollowingPanelJsonEvent()
    {
        using var temp = new TempDirectory();
        var (engine, clock) = CreateEngine();
        using var _ = engine;

        var store = new JsonConfigStore(temp.Config, logger: null, writeSuppression: engine);
        var saved = await store.SaveAsync(DefaultConfigGenerator.Create());
        Assert.True(saved.Succeeded);

        // The watcher event the atomic save would raise is ignored inside the self-write window.
        Assert.Equal(ReloadDecision.Ignore, engine.NotifyChange(Change(temp.Config.PanelFile, clock)));

        clock.Advance(SelfWrite + TimeSpan.FromMilliseconds(1));

        // Once the window closes, an externally-visible change is a normal debounce again.
        Assert.Equal(ReloadDecision.Debounce, engine.NotifyChange(Change(temp.Config.PanelFile, clock)));
    }

    [Fact]
    public async Task JsonConfigStore_SaveAsync_RecordsContentForNoOpComparison()
    {
        using var temp = new TempDirectory();
        var (engine, _) = CreateEngine();
        using var __ = engine;

        var store = new JsonConfigStore(temp.Config, logger: null, writeSuppression: engine);
        Assert.True((await store.SaveAsync(DefaultConfigGenerator.Create())).Succeeded);

        var onDisk = await File.ReadAllTextAsync(temp.Config.PanelFile);

        Assert.Equal(ReloadDecision.Ignore, engine.EvaluateCandidate(onDisk));
    }

    [Fact]
    public async Task JsonConfigStore_LoadAsync_SeedsBaselineSoIdenticalExternalWriteIsANoOp()
    {
        using var temp = new TempDirectory();
        var (engine, _) = CreateEngine();
        using var __ = engine;

        // Cold start: a valid panel.json already exists but this store has never saved it, so no
        // save has contributed a baseline content hash yet.
        var onDisk = JsonSerializer.Serialize(DefaultConfigGenerator.Create(), ConfigJson.Options);
        await File.WriteAllTextAsync(temp.Config.PanelFile, onDisk);

        var store = new JsonConfigStore(temp.Config, logger: null, writeSuppression: engine);
        var result = await store.LoadAsync();
        Assert.Equal(ConfigState.Current, result.State);

        // An external write of byte-identical content is a no-op from the first event (D2).
        Assert.Equal(ReloadDecision.Ignore, engine.EvaluateCandidate(onDisk));

        // Genuinely different content still reloads.
        Assert.Equal(ReloadDecision.Reload, engine.EvaluateCandidate(onDisk + Environment.NewLine));
    }

    [Fact]
    public async Task StateStore_SaveAsync_OpensTheSelfWriteWindow()
    {
        using var temp = new TempDirectory();
        var (engine, clock) = CreateEngine();
        using var _ = engine;

        var store = new StateStore(temp.Config, logger: null, writeSuppression: engine);
        Assert.True(await store.SaveAsync(new RuntimeState { CloseNotificationShown = true }));

        // The runtime-state write suppresses any concurrent panel.json event too (M2-RF-823).
        Assert.Equal(ReloadDecision.Ignore, engine.NotifyChange(Change(temp.Config.PanelFile, clock)));

        // state.json itself is outside the watcher filter (M2-RF-821) regardless of suppression.
        Assert.Equal(ReloadDecision.Ignore, engine.NotifyChange(Change(temp.Config.StateFile, clock)));

        clock.Advance(SelfWrite + TimeSpan.FromMilliseconds(1));
        Assert.Equal(ReloadDecision.Debounce, engine.NotifyChange(Change(temp.Config.PanelFile, clock)));
    }

    [Fact]
    public async Task Stores_WithoutSuppression_StillPersistNormally()
    {
        using var temp = new TempDirectory();

        var configStore = new JsonConfigStore(temp.Config, logger: null);
        Assert.True((await configStore.SaveAsync(DefaultConfigGenerator.Create())).Succeeded);
        Assert.True(File.Exists(temp.Config.PanelFile));

        var stateStore = new StateStore(temp.Config, logger: null);
        Assert.True(await stateStore.SaveAsync(new RuntimeState { CloseNotificationShown = true }));
        Assert.True(File.Exists(temp.Config.StateFile));
    }

    private static (ReloadDecisionEngine Engine, FakeTimeProvider Clock) CreateEngine()
    {
        var clock = new FakeTimeProvider();
        return (new ReloadDecisionEngine(clock, Debounce, SelfWrite), clock);
    }

    private static ConfigFileChange Change(string path, FakeTimeProvider clock) =>
        new(ConfigFileChangeKind.Changed, path, clock.UtcNow);
}
