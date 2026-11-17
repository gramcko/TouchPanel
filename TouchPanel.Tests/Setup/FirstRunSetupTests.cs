using System.IO;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Services.Setup;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Theme;
using TouchPanel.ViewModels.Setup;

namespace TouchPanel.Tests.Setup;

/// <summary>
/// First-run setup behavior (no UI automation): first-run detection (file absent vs present), the
/// view model's monitor listing/default selection, the single settings change built by Apply
/// (monitor + fallback + startup), preservation of every other section through the real
/// single-writer <see cref="SettingsCoordinator"/>, the skip and empty-monitor paths, and failure
/// isolation when the startup registration fails.
/// </summary>
public sealed class FirstRunSetupTests
{
    [Fact]
    public void IsFirstRun_IsTrueWhenTheConfigurationFileIsAbsent()
    {
        using var temp = new TempDirectory();

        Assert.True(FirstRunDetection.IsFirstRun(temp.Config.PanelFile));
    }

    [Fact]
    public void IsFirstRun_IsFalseWhenTheConfigurationFileExists()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Config.PanelFile, "{}");

        Assert.False(FirstRunDetection.IsFirstRun(temp.Config.PanelFile));
    }

    [Fact]
    public async Task InitializeAsync_ListsMonitorsAndPreselectsThePrimary()
    {
        var enumerator = new FakeMonitorEnumerator();
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(friendlyName: "Left", x: 0));
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(
            friendlyName: "Touch",
            x: 1920,
            primary: true,
            gdiName: @"\\.\DISPLAY2"));

        var viewModel = new FirstRunSetupViewModel(enumerator, new FakeSettingsService());

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasMonitors);
        Assert.Equal(2, viewModel.Monitors.Count);
        Assert.Equal("Touch", viewModel.SelectedMonitor!.DisplayName);
        Assert.True(viewModel.SelectedMonitor.IsPrimary);
        Assert.Contains("1920×1080", viewModel.Monitors[1].IdentitySummary);
        Assert.Contains("primary", viewModel.Monitors[1].IdentitySummary);
    }

    [Fact]
    public async Task ApplyAsync_BuildsOneChangeWithMonitorIdentityFallbackNoneAndStartup()
    {
        var enumerator = new FakeMonitorEnumerator();
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(friendlyName: "Touch", primary: true));
        var settings = new FakeSettingsService();
        var viewModel = new FirstRunSetupViewModel(enumerator, settings);

        await viewModel.InitializeAsync();
        viewModel.StartWithWindows = true;
        var applied = await viewModel.ApplyAsync();

        Assert.True(applied);
        Assert.True(viewModel.Applied);
        var change = Assert.Single(settings.Changes);
        Assert.True(change.SetMonitorMatch);
        Assert.Equal("DEL-A0BC-4353", change.MonitorMatch!.HardwareId);
        Assert.Equal("Touch", change.MonitorMatch.FriendlyName);
        Assert.NotNull(change.MonitorMatch.DevicePath);
        Assert.Equal(MonitorFallback.None, change.MonitorFallback);
        Assert.True(change.StartWithWindows);
    }

    [Fact]
    public async Task ApplyAsync_ThroughTheSettingsCoordinator_PreservesEveryOtherSection()
    {
        var config = DefaultConfigGenerator.Create();
        config.Window.AlwaysOnTop = true;
        config.Telemetry.PollIntervalSeconds = 2.5;
        config.Hotkeys.Add(new HotkeyConfig { Id = "toggle", Modifiers = ["Ctrl"], Key = "T" });
        config.Appearance.Theme = ThemeIds.Light;
        var pages = config.Pages;
        var appearance = config.Appearance;
        var hotkeys = config.Hotkeys;

        var store = new RecordingConfigStore();
        var target = new RecordingPanelConfigApplyTarget();
        var startup = new FakeStartupRegistration();
        var running = config;
        var coordinator = new SettingsCoordinator(
            () => running,
            store,
            new PanelConfigApplier(target),
            startup,
            updated => running = updated);

        var enumerator = new FakeMonitorEnumerator();
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(primary: true));
        var viewModel = new FirstRunSetupViewModel(enumerator, coordinator);

        await viewModel.InitializeAsync();
        viewModel.StartWithWindows = true;
        var applied = await viewModel.ApplyAsync();

        Assert.True(applied);
        Assert.Equal([true], startup.Applied);
        var saved = Assert.IsType<PanelConfig>(store.LastSavedConfig);
        Assert.Equal("DEL-A0BC-4353", saved.Monitor.Match!.HardwareId);
        Assert.Equal(MonitorFallback.None, saved.Monitor.Fallback);
        Assert.True(saved.Startup.StartWithWindows);

        // Every unrelated section is carried over unchanged.
        Assert.True(saved.Window.AlwaysOnTop);
        Assert.Equal(2.5, saved.Telemetry.PollIntervalSeconds);
        Assert.Same(pages, saved.Pages);
        Assert.Same(appearance, saved.Appearance);
        Assert.Same(hotkeys, saved.Hotkeys);
        Assert.Equal(ThemeIds.Light, saved.Appearance.Theme);
        Assert.Single(saved.Hotkeys);
        Assert.Same(saved, running);
    }

    [Fact]
    public async Task Skip_LeavesDefaultsAndWritesNothing()
    {
        var settings = new FakeSettingsService();
        var viewModel = new FirstRunSetupViewModel(new FakeMonitorEnumerator(), settings);
        await viewModel.InitializeAsync();

        viewModel.Skip();

        Assert.True(viewModel.Skipped);
        Assert.False(viewModel.Applied);
        Assert.Empty(settings.Changes);
    }

    [Fact]
    public async Task EmptyMonitorEnumeration_SkipsTheMonitorChoiceButStillSavesStartup()
    {
        var settings = new FakeSettingsService();
        var viewModel = new FirstRunSetupViewModel(new FakeMonitorEnumerator(), settings);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.HasMonitors);
        Assert.Null(viewModel.SelectedMonitor);

        viewModel.StartWithWindows = true;
        var applied = await viewModel.ApplyAsync();

        Assert.True(applied);
        var change = Assert.Single(settings.Changes);
        Assert.False(change.SetMonitorMatch);
        Assert.Null(change.MonitorMatch);
        Assert.Null(change.MonitorFallback);
        Assert.True(change.StartWithWindows);
    }

    [Fact]
    public async Task MonitorEnumerationFailure_DegradesToTheStartupChoiceOnly()
    {
        var enumerator = new FakeMonitorEnumerator
        {
            ExceptionToThrow = new InvalidOperationException("simulated enumeration failure"),
        };
        var settings = new FakeSettingsService();
        var viewModel = new FirstRunSetupViewModel(enumerator, settings);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.HasMonitors);
        Assert.Null(viewModel.SelectedMonitor);

        viewModel.StartWithWindows = true;
        Assert.True(await viewModel.ApplyAsync());
        var change = Assert.Single(settings.Changes);
        Assert.False(change.SetMonitorMatch);
        Assert.True(change.StartWithWindows);
    }

    [Fact]
    public async Task StartupRegistrationFailure_DoesNotCrashAndKeepsTheDefaults()
    {
        var config = DefaultConfigGenerator.Create();
        var store = new RecordingConfigStore();
        var startup = new FakeStartupRegistration
        {
            ThrowOnApply = new UnauthorizedAccessException("simulated registry failure"),
        };
        var coordinator = new SettingsCoordinator(
            () => config,
            store,
            new PanelConfigApplier(new RecordingPanelConfigApplyTarget()),
            startup,
            updated => config = updated);

        var enumerator = new FakeMonitorEnumerator();
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(primary: true));
        var viewModel = new FirstRunSetupViewModel(enumerator, coordinator);

        await viewModel.InitializeAsync();
        viewModel.StartWithWindows = true;
        var applied = await viewModel.ApplyAsync();

        Assert.False(applied);
        Assert.False(viewModel.Applied);
        Assert.Equal(0, store.SaveCallCount);
        Assert.Null(config.Monitor.Match);
        Assert.False(config.Startup.StartWithWindows);
    }
}
