using System.IO;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Single-writer hotkey persistence (M3-RF-615): a hotkey edit is saved and applied through the same
/// <see cref="SettingsCoordinator"/> path as any other settings change, and
/// <see cref="SettingsCoordinator.BuildUpdated"/> preserves every section it is not explicitly
/// changing, so a hotkey edit never resets window/startup/telemetry/monitor/pages (and vice versa).
/// A failed save changes nothing and never reaches the apply hook (M3-RF-617/F-M3-15).
/// </summary>
public sealed class SettingsCoordinatorHotkeysTests
{
    [Fact]
    public async Task ApplyHotkeys_SavesAppliesAndPublishesTheNewList()
    {
        var harness = Create();
        var hotkeys = new List<HotkeyConfig> { Hotkey("A", "Ctrl") };

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { Hotkeys = hotkeys });

        Assert.True(result.Succeeded);
        Assert.Equal(1, harness.Store.SaveCallCount);

        var saved = harness.Store.LastSavedConfig!;
        Assert.Equal("A", Assert.Single(saved.Hotkeys).Key);
        Assert.Same(hotkeys[0], saved.Hotkeys[0]);
        Assert.Contains("hotkeys", harness.Target.Steps);
        Assert.NotNull(harness.Target.LastHotkeys);
        Assert.Equal("A", Assert.Single(harness.Target.LastHotkeys!).Key);
        Assert.Same(result.Config, harness.Current.Config);
    }

    [Fact]
    public async Task ApplyHotkeys_PreservesEveryUnchangedSection()
    {
        var harness = Create();
        var original = harness.Current.Config!;
        original.Window.AlwaysOnTop = true;
        original.Telemetry.PollIntervalSeconds = 3.0;
        original.Startup.StartWithWindows = true;
        original.Monitor.Match = new MonitorIdentity { HardwareId = "DEL-A0BC" };
        original.Monitor.Fallback = MonitorFallback.Primary;
        var pageCount = original.Pages.Count;

        var result = await harness.Coordinator.ApplyAsync(
            new SettingsChange { Hotkeys = [Hotkey("A", "Ctrl")] });

        Assert.True(result.Succeeded);
        var saved = harness.Store.LastSavedConfig!;
        Assert.True(saved.Window.AlwaysOnTop);
        Assert.Equal(3.0, saved.Telemetry.PollIntervalSeconds);
        Assert.True(saved.Startup.StartWithWindows);
        Assert.Equal("DEL-A0BC", saved.Monitor.Match!.HardwareId);
        Assert.Equal(MonitorFallback.Primary, saved.Monitor.Fallback);
        Assert.Same(original.Pages, saved.Pages);
        Assert.Equal(pageCount, saved.Pages.Count);
    }

    [Fact]
    public async Task ApplyNonHotkeyChange_PreservesTheExistingHotkeys()
    {
        var harness = Create();
        var original = harness.Current.Config!;
        original.Hotkeys = [Hotkey("S", "Alt")];

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { AlwaysOnTop = true });

        Assert.True(result.Succeeded);
        var saved = harness.Store.LastSavedConfig!;
        Assert.Same(original.Hotkeys, saved.Hotkeys);
        Assert.Equal("S", Assert.Single(saved.Hotkeys).Key);
        Assert.True(saved.Window.AlwaysOnTop);
    }

    [Fact]
    public async Task ApplyHotkeys_SaveFailure_ChangesNothingAndNeverReachesTheApplyHook()
    {
        var harness = Create();
        var original = harness.Current.Config!;
        original.Hotkeys = [Hotkey("S", "Alt")];
        harness.Store.SaveSucceeds = false;

        var result = await harness.Coordinator.ApplyAsync(
            new SettingsChange { Hotkeys = [Hotkey("A", "Ctrl")] });

        Assert.False(result.Succeeded);
        Assert.Equal("settings.saveFailed", result.ErrorCode);
        Assert.Empty(harness.Target.Steps);
        Assert.Equal("S", Assert.Single(harness.Current.Config!.Hotkeys).Key);
        Assert.Equal("S", Assert.Single(original.Hotkeys).Key);
    }

    [Fact]
    public async Task ApplyHotkeys_StoreThrows_IsContainedAndChangesNothing()
    {
        var harness = Create();
        harness.Store.SaveHandler = _ => throw new IOException("simulated store failure");

        var result = await harness.Coordinator.ApplyAsync(
            new SettingsChange { Hotkeys = [Hotkey("A", "Ctrl")] });

        Assert.False(result.Succeeded);
        Assert.Empty(harness.Target.Steps);
        Assert.Empty(harness.Current.Config!.Hotkeys);
    }

    private static HotkeyConfig Hotkey(string key, params string[] modifiers) => new()
    {
        Id = string.Empty,
        Key = key,
        Modifiers = [.. modifiers],
        Action = new MuteActionConfig(),
    };

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

        return new Harness(coordinator, store, target, holder);
    }

    private sealed class Holder(PanelConfig? config)
    {
        public PanelConfig? Config { get; set; } = config;
    }

    private sealed record Harness(
        SettingsCoordinator Coordinator,
        RecordingConfigStore Store,
        RecordingPanelConfigApplyTarget Target,
        Holder Current);
}
