using System.IO;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Services.Startup;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Settings-apply orchestration (T-17, RF-092/M2-RF-921..925): a change is normalized with the
/// config validator, the start-with-Windows registration runs first, the candidate is persisted
/// atomically through the single-writer store, and only then is it published and applied through
/// the shared pipeline. Every failure leaves the running configuration untouched; the running
/// instance is never mutated before the save succeeds. Uses a recording store and apply target, so
/// no WPF or file system is involved (M2-RNF-124).
/// </summary>
public sealed class SettingsCoordinatorTests
{
    [Fact]
    public async Task ApplyAlwaysOnTop_SavesAppliesAndPublishes_WithoutMutatingTheRunningInstance()
    {
        var harness = Create();
        var original = harness.Current.Config!;

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { AlwaysOnTop = true });

        Assert.True(result.Succeeded);
        Assert.Equal(1, harness.Store.SaveCallCount);
        Assert.True(harness.Store.LastSavedConfig!.Window.AlwaysOnTop);
        Assert.True(harness.Target.LastWindow!.AlwaysOnTop);
        Assert.Contains("window-options", harness.Target.Steps);
        Assert.Same(result.Config, harness.Current.Config);

        // M2-RF-922: the running instance is never mutated before the save succeeds.
        Assert.False(original.Window.AlwaysOnTop);
        Assert.NotSame(original, result.Config);
        Assert.Same(original.Pages, result.Config!.Pages);
    }

    [Fact]
    public async Task ApplyStartWithWindows_AppliesRegistrationBeforeSaving()
    {
        var harness = Create();

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { StartWithWindows = true });

        Assert.True(result.Succeeded);
        Assert.Equal([true], harness.Startup.Applied);
        Assert.True(harness.Store.LastSavedConfig!.Startup.StartWithWindows);
        Assert.Equal(1, harness.Store.SaveCallCount);
    }

    [Fact]
    public async Task ApplyStartWithWindows_RegistrationFailure_DoesNotSaveOrPublish()
    {
        var harness = Create();
        harness.Startup.Result = StartupRegistrationResult.Failure("startup.write.failed", "The entry could not be created.");

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { StartWithWindows = true });

        Assert.False(result.Succeeded);
        Assert.Equal("startup.write.failed", result.ErrorCode);
        Assert.Equal(0, harness.Store.SaveCallCount);
        Assert.Empty(harness.Target.Steps);
        Assert.False(harness.Current.Config!.Startup.StartWithWindows);
    }

    [Fact]
    public async Task ApplyPollInterval_WithinRange_SavesExactValue()
    {
        var harness = Create();

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { PollIntervalSeconds = 2.5 });

        Assert.True(result.Succeeded);
        Assert.Equal(2.5, harness.Store.LastSavedConfig!.Telemetry.PollIntervalSeconds);
        Assert.Equal(2.5, harness.Target.LastTelemetry!.PollIntervalSeconds);
        Assert.Contains("telemetry-interval", harness.Target.Steps);
    }

    [Fact]
    public async Task ApplyPollInterval_OutOfRange_IsClampedByTheConfigValidatorRules()
    {
        var harness = Create();

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { PollIntervalSeconds = 99 });

        Assert.True(result.Succeeded);
        Assert.Equal(TelemetryConfig.MaxPollIntervalSeconds, harness.Store.LastSavedConfig!.Telemetry.PollIntervalSeconds);
        Assert.Equal(TelemetryConfig.MaxPollIntervalSeconds, harness.Current.Config!.Telemetry.PollIntervalSeconds);
    }

    [Fact]
    public async Task ApplyMonitor_WritesIdentityAndFallback()
    {
        var harness = Create();
        var identity = new MonitorIdentity
        {
            HardwareId = "DEL-A0BC-4353",
            DevicePath = @"\\?\DISPLAY#DELA0BC#5&2f1a3b4c&0&UID4353",
            FriendlyName = "DELL U2415",
        };

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange
        {
            SetMonitorMatch = true,
            MonitorMatch = identity,
            MonitorFallback = MonitorFallback.Primary,
        });

        Assert.True(result.Succeeded);
        var savedMonitor = harness.Store.LastSavedConfig!.Monitor;
        Assert.NotNull(savedMonitor.Match);
        Assert.Equal("DEL-A0BC-4353", savedMonitor.Match!.HardwareId);
        Assert.Equal("DELL U2415", savedMonitor.Match.FriendlyName);
        Assert.Equal(MonitorFallback.Primary, savedMonitor.Fallback);
        Assert.Contains("monitor-target", harness.Target.Steps);
    }

    [Fact]
    public async Task ApplyFallbackOnly_PreservesTheExistingMatch()
    {
        var harness = Create();
        harness.Current.Config!.Monitor.Match = new MonitorIdentity { HardwareId = "DEL-A0BC" };

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { MonitorFallback = MonitorFallback.Primary });

        Assert.True(result.Succeeded);
        Assert.Equal("DEL-A0BC", harness.Store.LastSavedConfig!.Monitor.Match!.HardwareId);
        Assert.Equal(MonitorFallback.Primary, harness.Store.LastSavedConfig.Monitor.Fallback);
    }

    [Fact]
    public async Task ApplySaveFailure_ChangesNothing()
    {
        var harness = Create();
        harness.Store.SaveSucceeds = false;

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { AlwaysOnTop = true });

        Assert.False(result.Succeeded);
        Assert.Equal("settings.saveFailed", result.ErrorCode);
        Assert.Empty(harness.Target.Steps);
        Assert.False(harness.Current.Config!.Window.AlwaysOnTop);
    }

    [Fact]
    public async Task ApplyStoreThrows_IsContainedAndNothingApplied()
    {
        var harness = Create();
        harness.Store.SaveHandler = _ => throw new IOException("simulated store failure");

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { AlwaysOnTop = true });

        Assert.False(result.Succeeded);
        Assert.Empty(harness.Target.Steps);
    }

    [Fact]
    public async Task Apply_WithoutRunningConfig_Fails()
    {
        var store = new RecordingConfigStore();
        var target = new RecordingPanelConfigApplyTarget();
        var coordinator = new SettingsCoordinator(
            () => null,
            store,
            new PanelConfigApplier(target),
            new FakeStartupRegistration(),
            _ => { });

        var result = await coordinator.ApplyAsync(new SettingsChange { AlwaysOnTop = true });

        Assert.False(result.Succeeded);
        Assert.Equal("settings.noConfig", result.ErrorCode);
        Assert.Equal(0, store.SaveCallCount);
    }

    private static Harness Create()
    {
        var store = new RecordingConfigStore();
        var target = new RecordingPanelConfigApplyTarget();
        var applier = new PanelConfigApplier(target);
        var startup = new FakeStartupRegistration();

        var holder = new Holder(DefaultConfigGenerator.Create());
        var coordinator = new SettingsCoordinator(
            () => holder.Config,
            store,
            applier,
            startup,
            updated => holder.Config = updated);

        return new Harness(coordinator, store, target, startup, holder);
    }

    private sealed class Holder(PanelConfig? config)
    {
        public PanelConfig? Config { get; set; } = config;
    }

    private sealed record Harness(
        SettingsCoordinator Coordinator,
        RecordingConfigStore Store,
        RecordingPanelConfigApplyTarget Target,
        FakeStartupRegistration Startup,
        Holder Current);
}
