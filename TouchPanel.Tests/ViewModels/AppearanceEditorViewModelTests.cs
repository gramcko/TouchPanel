using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Theme;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// M3-RF-850 coverage for the appearance selection view model: theme/icon-pack selection and accent
/// set/clear persist validated changes through the single-writer <see cref="ISettingsService"/> path,
/// invalid accent input is blocked inline, a failed apply reverts the UI, and the initial values do
/// not trigger a redundant save.
/// </summary>
public sealed class AppearanceEditorViewModelTests
{
    [Fact]
    public void InitialValues_DoNotTriggerAnApply()
    {
        var settings = new FakeSettingsService();
        var current = new AppearanceConfig { Theme = "light", AccentColor = "#AABBCC", IconPack = "filled" };

        _ = new AppearanceEditorViewModel(settings, () => current);

        Assert.Empty(settings.Changes);
    }

    [Fact]
    public void Options_ListEveryBuiltInThemeAndIconPack()
    {
        var settings = new FakeSettingsService();
        var viewModel = new AppearanceEditorViewModel(settings, () => new AppearanceConfig());

        Assert.Equal(ThemeIds.All, viewModel.ThemeOptions);
        Assert.Equal(IconPackIds.All, viewModel.IconPackOptions);

        foreach (var themeId in new[] { ThemeIds.Midnight, ThemeIds.Nord, ThemeIds.Oled })
        {
            Assert.Contains(themeId, viewModel.ThemeOptions);
        }

        foreach (var packId in new[] { IconPackIds.Outline, IconPackIds.Duotone })
        {
            Assert.Contains(packId, viewModel.IconPackOptions);
        }
    }

    [Theory]
    [InlineData(ThemeIds.Midnight, IconPackIds.Outline)]
    [InlineData(ThemeIds.Nord, IconPackIds.Duotone)]
    [InlineData(ThemeIds.Oled, IconPackIds.Filled)]
    public async Task SelectingANewThemeAndPack_AppliesPersistsAndIsSeededOnReload(
        string themeId,
        string packId)
    {
        var settings = new FakeSettingsService();
        var current = new AppearanceConfig { Theme = ThemeIds.Dark, IconPack = IconPackIds.Default };
        settings.Handler = change =>
        {
            current = new AppearanceConfig
            {
                Theme = change.Appearance!.Theme,
                IconPack = change.Appearance.IconPack,
                AccentColor = change.Appearance.AccentColor,
            };
            return SettingsApplyResult.Success(new PanelConfig());
        };

        var viewModel = new AppearanceEditorViewModel(settings, () => current);
        viewModel.SelectedTheme = themeId;
        viewModel.SelectedIconPack = packId;
        await viewModel.ApplySelectionInternalAsync();

        var change = settings.Changes[^1];
        Assert.Equal(themeId, change.Appearance!.Theme);
        Assert.Equal(packId, change.Appearance.IconPack);
        Assert.False(viewModel.StatusIsError);

        // The selection persisted through the single-writer path, so a freshly constructed editor
        // (as after reopening the window) shows the same theme and pack without a redundant save.
        var appliedCount = settings.Changes.Count;
        var reloaded = new AppearanceEditorViewModel(settings, () => current);
        Assert.Equal(themeId, reloaded.SelectedTheme);
        Assert.Equal(packId, reloaded.SelectedIconPack);
        Assert.Equal(appliedCount, settings.Changes.Count);
    }

    [Fact]
    public async Task SelectingATheme_AppliesTheChangeThroughTheSingleWriter()
    {
        var settings = new FakeSettingsService();
        var viewModel = new AppearanceEditorViewModel(settings, () => new AppearanceConfig { Theme = "dark" });

        viewModel.SelectedTheme = "light";
        await viewModel.ApplySelectionInternalAsync();

        var change = Assert.Single(settings.Changes);
        Assert.NotNull(change.Appearance);
        Assert.Equal("light", change.Appearance!.Theme);
        Assert.Equal("default", change.Appearance.IconPack);
        Assert.True(viewModel.HasStatusMessage);
        Assert.False(viewModel.StatusIsError);
    }

    [Fact]
    public async Task SelectingATheme_SendsOnlyAppearance_SoTheCoordinatorPreservesEveryOtherSection()
    {
        var settings = new FakeSettingsService();
        var viewModel = new AppearanceEditorViewModel(settings, () => new AppearanceConfig { Theme = "dark" });

        viewModel.SelectedTheme = "light";
        await viewModel.ApplySelectionInternalAsync();

        // M3-RF-615/M3-RF-841: an appearance edit carries no other section, so BuildUpdated keeps
        // window/startup/telemetry/monitor/hotkeys untouched.
        var change = Assert.Single(settings.Changes);
        Assert.NotNull(change.Appearance);
        Assert.Null(change.Hotkeys);
        Assert.Null(change.AlwaysOnTop);
        Assert.Null(change.StartWithWindows);
        Assert.Null(change.PollIntervalSeconds);
        Assert.Null(change.MonitorFallback);
        Assert.False(change.SetMonitorMatch);
    }

    [Fact]
    public async Task SelectingAnIconPack_AppliesTheChangeThroughTheSingleWriter()
    {
        var settings = new FakeSettingsService();
        var viewModel = new AppearanceEditorViewModel(settings, () => new AppearanceConfig());

        viewModel.SelectedIconPack = "filled";
        await viewModel.ApplySelectionInternalAsync();

        var change = Assert.Single(settings.Changes);
        Assert.Equal("filled", change.Appearance!.IconPack);
    }

    [Fact]
    public async Task InvalidAccent_IsBlockedInline_AndNeverApplied()
    {
        var settings = new FakeSettingsService();
        var viewModel = new AppearanceEditorViewModel(settings, () => new AppearanceConfig())
        {
            AccentText = "not-a-color",
        };

        await viewModel.ApplyAccentAsync();

        Assert.True(viewModel.HasAccentError);
        Assert.Empty(settings.Changes);
    }

    [Fact]
    public async Task ValidAccent_IsNormalizedAndApplied()
    {
        var settings = new FakeSettingsService();
        var viewModel = new AppearanceEditorViewModel(settings, () => new AppearanceConfig())
        {
            AccentText = "#aabbcc",
        };

        await viewModel.ApplyAccentAsync();

        Assert.False(viewModel.HasAccentError);
        var change = Assert.Single(settings.Changes);
        Assert.Equal("#AABBCC", change.Appearance!.AccentColor);
    }

    [Fact]
    public async Task ClearAccent_AppliesNull()
    {
        var settings = new FakeSettingsService();
        var current = new AppearanceConfig { AccentColor = "#112233" };
        var viewModel = new AppearanceEditorViewModel(settings, () => current);

        await viewModel.ClearAccentInternalAsync();

        var change = Assert.Single(settings.Changes);
        Assert.Null(change.Appearance!.AccentColor);
        Assert.Equal(string.Empty, viewModel.AccentText);
    }

    [Fact]
    public async Task FailedApply_RevertsTheSelection_AndSurfacesTheError()
    {
        var settings = new FakeSettingsService
        {
            Handler = _ => SettingsApplyResult.Failure("settings.saveFailed", "The appearance could not be saved."),
        };
        var viewModel = new AppearanceEditorViewModel(settings, () => new AppearanceConfig { Theme = "dark" });

        viewModel.SelectedTheme = "light";
        await viewModel.ApplySelectionInternalAsync();

        Assert.Equal("dark", viewModel.SelectedTheme);
        Assert.True(viewModel.StatusIsError);
        Assert.Equal("The appearance could not be saved.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task NoChange_DoesNotApply()
    {
        var settings = new FakeSettingsService();
        var viewModel = new AppearanceEditorViewModel(settings, () => new AppearanceConfig { Theme = "dark" });

        viewModel.SelectedTheme = "dark";
        await viewModel.ApplySelectionInternalAsync();

        Assert.Empty(settings.Changes);
    }

    [Fact]
    public void PageTransitionOptions_ListEveryModeInAStableOrder()
    {
        var settings = new FakeSettingsService();
        var viewModel = new AppearanceEditorViewModel(settings, () => new AppearanceConfig());

        Assert.Equal(PageTransitionModes.All, viewModel.PageTransitionOptions);
        Assert.Equal(PageTransitionModes.Default, viewModel.SelectedPageTransition);
    }

    [Fact]
    public async Task SelectingPageTransition_AppliesItThroughTheSharedSettingsPath()
    {
        var settings = new FakeSettingsService();
        var window = new WindowConfig { AlwaysOnTop = true, PageTransition = PageTransitionModes.Slide };
        var viewModel = new AppearanceEditorViewModel(
            settings,
            () => new AppearanceConfig(),
            currentWindow: () => window);

        viewModel.SelectedPageTransition = "SLIDEUP";
        await viewModel.ApplySelectionInternalAsync();

        var change = Assert.Single(settings.Changes);
        Assert.Equal(PageTransitionModes.SlideUp, change.PageTransition);
        Assert.Equal(PageTransitionModes.SlideUp, viewModel.SelectedPageTransition);
        Assert.False(viewModel.StatusIsError);
    }

    [Fact]
    public async Task SelectingPageTransition_SeededFromTheRunningValue_DoesNotApply()
    {
        var settings = new FakeSettingsService();
        var window = new WindowConfig { PageTransition = PageTransitionModes.Fade };
        _ = new AppearanceEditorViewModel(settings, () => new AppearanceConfig(), currentWindow: () => window);

        await Task.Yield();

        Assert.Empty(settings.Changes);
    }

    [Fact]
    public async Task FailedPageTransitionApply_RevertsTheSelection()
    {
        var settings = new FakeSettingsService
        {
            Handler = _ => SettingsApplyResult.Failure("settings.saveFailed", "The appearance could not be saved."),
        };
        var viewModel = new AppearanceEditorViewModel(
            settings,
            () => new AppearanceConfig(),
            currentWindow: () => new WindowConfig { PageTransition = PageTransitionModes.Slide });

        viewModel.SelectedPageTransition = PageTransitionModes.Zoom;
        await viewModel.ApplySelectionInternalAsync();

        Assert.Equal(PageTransitionModes.Slide, viewModel.SelectedPageTransition);
        Assert.True(viewModel.StatusIsError);
    }

    [Fact]
    public void PageTransitionDropdown_UsesTheDocumentedModes()
    {
        Assert.Contains("none", PageTransitionModes.All);
        Assert.Contains("slide", PageTransitionModes.All);
        Assert.Contains("fade", PageTransitionModes.All);
        Assert.Contains("slideUp", PageTransitionModes.All);
        Assert.Contains("zoom", PageTransitionModes.All);
    }
}
