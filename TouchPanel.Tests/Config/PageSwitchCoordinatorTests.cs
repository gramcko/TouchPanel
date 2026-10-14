using Serilog.Events;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Page-switch orchestration (T-16, RF-091/M2-RF-912/M2-RF-914): a switch resolves the page with the
/// first-page fallback, updates <c>activePageId</c>, rebuilds the live page, reconciles tracked
/// metrics, and persists through the store; a same-page request is a no-op, a switch is refused while
/// editing, and rebuild/store failures are isolated.
/// </summary>
public sealed class PageSwitchCoordinatorTests
{
    [Fact]
    public async Task Switch_ChangesTheActivePage_RebuildsReconcilesAndPersists()
    {
        var config = TwoPages();
        var target = new RecordingPanelConfigApplyTarget();
        var store = new RecordingConfigStore();
        var coordinator = new PageSwitchCoordinator(() => config, store, target);

        var result = await coordinator.SwitchAsync("second");

        Assert.True(result.Changed);
        Assert.Equal("second", result.ResolvedPageId);
        Assert.True(result.Persisted);
        Assert.False(result.RebuildFailed);

        Assert.Equal("second", config.ActivePageId);
        Assert.Equal(["active-page", "tracked-metrics"], target.Steps);
        Assert.Same(config, target.LastRebuilt);
        Assert.Same(config, target.LastTracked);
        Assert.Equal(1, store.SaveCallCount);
        Assert.Same(config, store.LastSavedConfig);
    }

    [Fact]
    public async Task Switch_UnknownId_FallsBackToTheFirstPageAndLogs()
    {
        var config = TwoPages(activePageId: "second");
        var (logger, sink) = TestLoggerFactory.Create();
        var coordinator = new PageSwitchCoordinator(
            () => config,
            new RecordingConfigStore(),
            new RecordingPanelConfigApplyTarget(),
            logger: logger);

        var result = await coordinator.SwitchAsync("does-not-exist");

        Assert.True(result.Changed);
        Assert.Equal("first", result.ResolvedPageId);
        Assert.Equal("first", config.ActivePageId);
        Assert.Contains(
            sink.Events,
            e => e.Level == LogEventLevel.Warning
                && e.RenderMessage().Contains("falling back to the first page", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Switch_AlreadyActive_IsANoOp_WithNoRebuildOrSave()
    {
        var config = TwoPages(activePageId: "first");
        var target = new RecordingPanelConfigApplyTarget();
        var store = new RecordingConfigStore();
        var coordinator = new PageSwitchCoordinator(() => config, store, target);

        var result = await coordinator.SwitchAsync("first");

        Assert.False(result.Changed);
        Assert.Equal("first", result.ResolvedPageId);
        Assert.Empty(target.Steps);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public async Task Switch_RefusedWhileEditing_DoesNotChangeRebuildOrSave()
    {
        var config = TwoPages();
        var target = new RecordingPanelConfigApplyTarget();
        var store = new RecordingConfigStore();
        var coordinator = new PageSwitchCoordinator(
            () => config,
            store,
            target,
            canSwitch: () => false);

        var result = await coordinator.SwitchAsync("second");

        Assert.False(result.Changed);
        Assert.Equal("first", config.ActivePageId);
        Assert.Empty(target.Steps);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public async Task Switch_RebuildFailure_IsIsolated_AndTheSelectionIsStillPersisted()
    {
        var config = TwoPages();
        var target = new RecordingPanelConfigApplyTarget { FailStep = "active-page" };
        var store = new RecordingConfigStore();
        var coordinator = new PageSwitchCoordinator(() => config, store, target);

        var result = await coordinator.SwitchAsync("second");

        Assert.True(result.Changed);
        Assert.Equal("second", result.ResolvedPageId);
        Assert.True(result.RebuildFailed);
        Assert.True(result.Persisted);
        Assert.Equal("second", config.ActivePageId);
        Assert.Equal(["tracked-metrics"], target.Steps);
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public async Task Switch_SaveFailure_ReportsChangedButNotPersisted()
    {
        var config = TwoPages();
        var target = new RecordingPanelConfigApplyTarget();
        var store = new RecordingConfigStore { SaveSucceeds = false };
        var coordinator = new PageSwitchCoordinator(() => config, store, target);

        var result = await coordinator.SwitchAsync("second");

        Assert.True(result.Changed);
        Assert.Equal("second", result.ResolvedPageId);
        Assert.False(result.Persisted);
        Assert.Equal("second", config.ActivePageId);
    }

    [Fact]
    public async Task Switch_WithoutAConfiguration_IsANoOp()
    {
        var target = new RecordingPanelConfigApplyTarget();
        var store = new RecordingConfigStore();
        var coordinator = new PageSwitchCoordinator(() => null, store, target);

        var result = await coordinator.SwitchAsync("second");

        Assert.False(result.Changed);
        Assert.Empty(target.Steps);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public void ResolveActivePageId_UnknownConfiguredId_FallsBackToTheFirstPage()
    {
        var config = TwoPages(activePageId: "missing");

        Assert.Equal("first", PageSwitchCoordinator.ResolveActivePageId(config));
    }

    [Fact]
    public void ResolveActivePageId_NoPages_ReturnsEmpty()
    {
        var config = new PanelConfig { SchemaVersion = PanelConfig.CurrentSchemaVersion, Pages = [] };

        Assert.Equal(string.Empty, PageSwitchCoordinator.ResolveActivePageId(config));
    }

    private static PanelConfig TwoPages(string activePageId = "first") => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = activePageId,
        Pages =
        [
            new PageConfig { Id = "first", Widgets = [] },
            new PageConfig { Id = "second", Widgets = [] },
        ],
    };
}
