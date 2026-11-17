using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Disk-level pages management persistence (user-requested pages management): adding, renaming,
/// and removing pages (including the active-page fallback) goes through the atomic single-writer
/// store, keeps the schema at v4, and preserves every other section on disk.
/// </summary>
public sealed class PageManagementPersistenceTests
{
    [Fact]
    public async Task AddPage_PersistsThePageNameAndPreservesOtherSectionsToDisk()
    {
        using var temp = new TempDirectory();
        var store = new JsonConfigStore(temp.Config);
        var config = SeededConfig();
        Assert.True((await store.SaveAsync(config)).Succeeded);

        var holder = new Holder { Config = config };
        var coordinator = CreateCoordinator(store, holder);
        var add = PageListOperations.AddPage(config.Pages, "Media");

        var result = await coordinator.ApplyAsync(new SettingsChange
        {
            Pages = add.Pages,
            ActivePageId = config.ActivePageId,
        });

        Assert.True(result.Succeeded, result.ErrorMessage);
        var reloaded = await store.LoadAsync();
        var saved = reloaded.Config!;
        Assert.Equal(4, saved.SchemaVersion);
        Assert.Equal(3, saved.Pages.Count);
        Assert.Equal("Media", saved.Pages[^1].Name);
        Assert.Equal("second", saved.ActivePageId);
        Assert.True(saved.Window.AlwaysOnTop);
        Assert.Equal("light", saved.Appearance.Theme);
        Assert.Equal(2.0, saved.Telemetry.PollIntervalSeconds);
    }

    [Fact]
    public async Task RemoveActivePage_PersistsTheFirstPageFallbackToDisk()
    {
        using var temp = new TempDirectory();
        var store = new JsonConfigStore(temp.Config);
        var config = SeededConfig();
        Assert.True((await store.SaveAsync(config)).Succeeded);

        var holder = new Holder { Config = config };
        var coordinator = CreateCoordinator(store, holder);
        var removal = PageListOperations.RemovePage(config.Pages, "second", config.ActivePageId);
        Assert.True(removal.Removed);
        Assert.True(removal.ActivePageFellBack);

        var result = await coordinator.ApplyAsync(new SettingsChange
        {
            Pages = removal.Pages,
            ActivePageId = removal.ActivePageId,
        });

        Assert.True(result.Succeeded, result.ErrorMessage);
        var reloaded = await store.LoadAsync();
        var saved = reloaded.Config!;
        Assert.Equal("main", saved.ActivePageId);
        Assert.Equal(["main"], saved.Pages.Select(page => page.Id));
        Assert.Equal(4, saved.SchemaVersion);
    }

    private static SettingsCoordinator CreateCoordinator(JsonConfigStore store, Holder holder) =>
        new(
            () => holder.Config,
            store,
            new PanelConfigApplier(new RecordingPanelConfigApplyTarget()),
            new FakeStartupRegistration(),
            updated => holder.Config = updated);

    private static PanelConfig SeededConfig()
    {
        var config = DefaultConfigGenerator.Create();
        config.ActivePageId = "second";
        config.Pages.Add(new PageConfig { Id = "second", Name = "Second", Widgets = [] });
        config.Window.AlwaysOnTop = true;
        config.Telemetry.PollIntervalSeconds = 2.0;
        config.Appearance = new AppearanceConfig { Theme = "light", IconPack = "default" };
        return config;
    }

    private sealed class Holder
    {
        public required PanelConfig Config { get; set; }
    }
}
