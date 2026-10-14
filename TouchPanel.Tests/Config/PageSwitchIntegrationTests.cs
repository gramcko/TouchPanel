using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Temp-directory page-switch persistence (T-16, M2-RF-912/M2-RF-915): switching writes the new
/// <c>activePageId</c> atomically through the real store, and because the store opens the
/// app-originated self-write window, the resulting <c>panel.json</c> event is ignored by the hot
/// reload decision engine (no reload loop).
/// </summary>
public sealed class PageSwitchIntegrationTests
{
    [Fact]
    public async Task Switch_PersistsActivePageId_AndTheSelfSaveDoesNotTriggerReload()
    {
        using var temp = new TempDirectory();
        var engine = new ReloadDecisionEngine();
        var store = new JsonConfigStore(temp.Config, writeSuppression: engine);
        var config = TwoPages();

        var firstSave = await store.SaveAsync(config);
        Assert.True(firstSave.Succeeded);

        var coordinator = new PageSwitchCoordinator(
            () => config,
            store,
            new RecordingPanelConfigApplyTarget());

        var result = await coordinator.SwitchAsync("second");
        Assert.True(result.Changed);
        Assert.True(result.Persisted);

        // The persisted file carries the new active page; a fresh load reads it back (M2-RF-912).
        var reloaded = await store.LoadAsync();
        Assert.Equal("second", reloaded.Config!.ActivePageId);

        // M2-RF-915/M2-RF-823: the app-originated save is suppressed, so the watcher's event for
        // panel.json must not lead to a reload.
        var decision = engine.NotifyChange(new ConfigFileChange(
            ConfigFileChangeKind.Changed,
            temp.Config.PanelFile,
            TimeProvider.System.GetUtcNow()));
        Assert.Equal(ReloadDecision.Ignore, decision);

        engine.Dispose();
    }

    private static PanelConfig TwoPages() => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = "first",
        Pages =
        [
            new PageConfig { Id = "first", Widgets = [] },
            new PageConfig { Id = "second", Widgets = [] },
        ],
    };
}
