using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Validation;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Paging;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Page-transition configuration coverage (user-requested addition): the mode ids are compared
/// case-insensitively and normalized to their canonical spelling, an unknown value falls back to
/// <c>slide</c> with a warning, the property is additive (an older file without it loads with the
/// default), the single-writer settings path persists and applies it, and the page-switch path
/// carries the direction to the apply target.
/// </summary>
public sealed class PageTransitionConfigTests
{
    [Fact]
    public void Modes_ExposeTheFixedAllowlistAndSlideDefault()
    {
        Assert.Equal(["none", "slide", "fade", "slideUp", "zoom"], PageTransitionModes.All);
        Assert.Equal(PageTransitionModes.Slide, PageTransitionModes.Default);
        Assert.Equal(PageTransitionKind.Slide, PageTransitionModes.Parse(new WindowConfig().PageTransition));
    }

    [Theory]
    [InlineData("none", "none", PageTransitionKind.None)]
    [InlineData("slide", "slide", PageTransitionKind.Slide)]
    [InlineData("FADE", "fade", PageTransitionKind.Fade)]
    [InlineData("slideup", "slideUp", PageTransitionKind.SlideUp)]
    [InlineData("  Zoom  ", "zoom", PageTransitionKind.Zoom)]
    public void TryNormalize_IsCaseInsensitiveAndCanonical(string configured, string canonical, PageTransitionKind kind)
    {
        Assert.True(PageTransitionModes.IsKnown(configured));
        Assert.True(PageTransitionModes.TryNormalize(configured, out var normalized));
        Assert.Equal(canonical, normalized);
        Assert.Equal(canonical, PageTransitionModes.Normalize(configured));
        Assert.Equal(kind, PageTransitionModes.Parse(configured));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("explode")]
    [InlineData("slide-left")]
    public void TryNormalize_UnknownOrMissing_FallsBackToSlide(string? configured)
    {
        Assert.False(PageTransitionModes.TryNormalize(configured, out var normalized));
        Assert.Equal(PageTransitionModes.Slide, normalized);
        Assert.Equal(PageTransitionModes.Slide, PageTransitionModes.Normalize(configured));
        Assert.Equal(PageTransitionKind.Slide, PageTransitionModes.Parse(configured));
    }

    [Fact]
    public void WindowConfig_MissingValue_DefaultsToSlide()
    {
        Assert.Equal(PageTransitionModes.Slide, new WindowConfig().PageTransition);
        Assert.Equal(PageTransitionModes.Slide, new WindowConfig { PageTransition = null! }.PageTransition);
    }

    [Fact]
    public void OlderJsonFile_WithoutTheProperty_LoadsTheDefault()
    {
        const string json = """
            {
              "schemaVersion": 4,
              "activePageId": "main",
              "window": { "alwaysOnTop": true },
              "pages": [ { "id": "main", "widgets": [] } ]
            }
            """;

        var config = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.True(config.Window.AlwaysOnTop);
        Assert.Equal(PageTransitionModes.Slide, config.Window.PageTransition);
    }

    [Fact]
    public void PageTransition_RoundTripsThroughJson()
    {
        var config = DefaultConfigGenerator.Create();
        config.Window.PageTransition = PageTransitionModes.SlideUp;

        var json = JsonSerializer.Serialize(config, ConfigJson.Options);
        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.Contains("\"pageTransition\": \"slideUp\"", json, StringComparison.Ordinal);
        Assert.Equal(PageTransitionModes.SlideUp, restored.Window.PageTransition);
    }

    [Fact]
    public void Validator_UnknownValue_WarnsAndNormalizesToSlide()
    {
        var config = DefaultConfigGenerator.Create();
        config.Window.PageTransition = "warp";

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal(PageTransitionModes.Slide, config.Window.PageTransition);
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Code == "config.window.pageTransition"
                && diagnostic.Severity == ConfigDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("'warp'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_KnownValueDifferentCasing_NormalizesWithoutWarning()
    {
        var config = DefaultConfigGenerator.Create();
        config.Window.PageTransition = "FADE";

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal(PageTransitionModes.Fade, config.Window.PageTransition);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.window.pageTransition");
    }

    [Fact]
    public async Task Settings_PersistsAndAppliesPageTransition_PreservingOtherSections()
    {
        var store = new RecordingConfigStore();
        var target = new RecordingPanelConfigApplyTarget();
        var current = DefaultConfigGenerator.Create();
        current.Window.AlwaysOnTop = true;
        current.Appearance = new AppearanceConfig { Theme = "nord" };
        var coordinator = new SettingsCoordinator(
            () => current,
            store,
            new PanelConfigApplier(target),
            new FakeStartupRegistration(),
            updated => current = updated);

        var result = await coordinator.ApplyAsync(new SettingsChange { PageTransition = "zoom" });

        Assert.True(result.Succeeded);
        var saved = store.LastSavedConfig!;
        Assert.Equal("zoom", saved.Window.PageTransition);
        Assert.True(saved.Window.AlwaysOnTop);
        Assert.Equal("nord", saved.Appearance.Theme);
        Assert.Contains("window-options", target.Steps);
        Assert.Equal("zoom", target.LastWindow!.PageTransition);
    }

    [Fact]
    public async Task Settings_UnrelatedChange_PreservesThePageTransition()
    {
        var store = new RecordingConfigStore();
        var current = DefaultConfigGenerator.Create();
        current.Window.PageTransition = "fade";
        var coordinator = new SettingsCoordinator(
            () => current,
            store,
            new PanelConfigApplier(new RecordingPanelConfigApplyTarget()),
            new FakeStartupRegistration(),
            updated => current = updated);

        var result = await coordinator.ApplyAsync(new SettingsChange { AlwaysOnTop = true });

        Assert.True(result.Succeeded);
        Assert.Equal("fade", store.LastSavedConfig!.Window.PageTransition);
    }

    [Fact]
    public async Task PageSwitch_ThreadsTheDirectionToTheApplyTarget()
    {
        var config = DefaultConfigGenerator.Create();
        config.Pages.Add(new PageConfig { Id = "second" });
        config.ActivePageId = config.Pages[0].Id;
        var target = new RecordingPanelConfigApplyTarget();
        var coordinator = new PageSwitchCoordinator(
            () => config,
            new RecordingConfigStore(),
            target);

        var result = await coordinator.SwitchAsync("second", PageTransitionDirection.Backward);

        Assert.True(result.Changed);
        Assert.Equal(PageTransitionDirection.Backward, target.LastDirection);
    }

    [Fact]
    public async Task PageSwitch_WithoutDirection_DefaultsToForward()
    {
        var config = DefaultConfigGenerator.Create();
        config.Pages.Add(new PageConfig { Id = "second" });
        config.ActivePageId = config.Pages[0].Id;
        var target = new RecordingPanelConfigApplyTarget();
        var coordinator = new PageSwitchCoordinator(
            () => config,
            new RecordingConfigStore(),
            target);

        _ = await coordinator.SwitchAsync("second");

        Assert.Equal(PageTransitionDirection.Forward, target.LastDirection);
    }
}
