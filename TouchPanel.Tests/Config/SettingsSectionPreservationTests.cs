using System.IO;
using Serilog;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Cross-section preservation for the single-writer settings path (M3-RF-615, M4/D-25, M2-RF-922):
/// every settings/appearance/hotkey edit must preserve <b>all</b> sections it does not explicitly
/// change, overlapping edits must be serialized so the last save cannot silently drop the other
/// edit's section, and a hotkey edit followed by an appearance edit must keep the hotkeys both in
/// the saved file and in the live re-registration hook.
/// </summary>
public sealed class SettingsSectionPreservationTests
{
    public static TheoryData<string> ChangeKinds() =>
    [
        "alwaysOnTop",
        "startWithWindows",
        "pollInterval",
        "monitor",
        "hotkeys",
        "appearance",
    ];

    [Theory]
    [MemberData(nameof(ChangeKinds))]
    public async Task Change_PreservesEveryUnchangedSection(string which)
    {
        var store = new RecordingConfigStore();
        var target = new RecordingPanelConfigApplyTarget();
        var holder = new Holder { Config = SeededConfig() };
        var coordinator = new SettingsCoordinator(
            () => holder.Config,
            store,
            new PanelConfigApplier(target),
            new FakeStartupRegistration(),
            updated => holder.Config = updated);

        var change = which switch
        {
            "alwaysOnTop" => new SettingsChange { AlwaysOnTop = false },
            "startWithWindows" => new SettingsChange { StartWithWindows = false },
            "pollInterval" => new SettingsChange { PollIntervalSeconds = 3.5 },
            "monitor" => new SettingsChange { MonitorFallback = MonitorFallback.None },
            "hotkeys" => new SettingsChange { Hotkeys = [Hotkey("Z", "Ctrl")] },
            "appearance" => new SettingsChange { Appearance = new AppearanceConfig { Theme = "light", IconPack = "default" } },
            _ => throw new InvalidOperationException($"Unknown change '{which}'."),
        };

        var result = await coordinator.ApplyAsync(change);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var saved = store.LastSavedConfig!;

        // Root-level sections never change.
        Assert.Equal(PanelConfig.CurrentSchemaVersion, saved.SchemaVersion);
        Assert.Equal("main", saved.ActivePageId);
        Assert.Single(saved.Pages);

        if (which != "alwaysOnTop")
        {
            Assert.True(saved.Window.AlwaysOnTop, "window.alwaysOnTop was reset");
        }

        if (which != "startWithWindows")
        {
            Assert.True(saved.Startup.StartWithWindows, "startup.startWithWindows was reset");
        }

        if (which != "pollInterval")
        {
            Assert.Equal(2.0, saved.Telemetry.PollIntervalSeconds);
        }

        if (which != "monitor")
        {
            Assert.Equal("DEL-A0BC", saved.Monitor.Match!.HardwareId);
            Assert.Equal(MonitorFallback.Primary, saved.Monitor.Fallback);
        }

        if (which != "hotkeys")
        {
            var hotkey = Assert.Single(saved.Hotkeys);
            Assert.Equal("A", hotkey.Key);
            Assert.Equal(["Alt"], hotkey.Modifiers);
        }

        if (which != "appearance")
        {
            Assert.Equal("highContrast", saved.Appearance.Theme);
            Assert.Equal("filled", saved.Appearance.IconPack);
            Assert.Equal("#112233", saved.Appearance.AccentColor);
        }
    }

    [Fact]
    public async Task OverlappingApplies_AreSerialized_AndBothSectionsSurvive()
    {
        // The store guards only the physical write; without serializing the whole
        // read-build-save-publish sequence the second apply would build from the same pre-apply
        // snapshot and the last save would drop the other section (M3-RF-615/M2-RF-922).
        var firstSaveGate = new TaskCompletionSource<ConfigSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingConfigStore();
        var saveCount = 0;
        store.SaveAsyncHandler = _ =>
        {
            var call = Interlocked.Increment(ref saveCount);
            return call == 1
                ? new ValueTask<ConfigSaveResult>(firstSaveGate.Task)
                : ValueTask.FromResult(ConfigSaveResult.Success());
        };

        var target = new RecordingPanelConfigApplyTarget();
        var holder = new Holder { Config = DefaultConfigGenerator.Create() };
        var coordinator = new SettingsCoordinator(
            () => holder.Config,
            store,
            new PanelConfigApplier(target),
            new FakeStartupRegistration(),
            updated => holder.Config = updated);

        // The hotkey apply blocks inside the store save while the appearance apply starts.
        var hotkeysTask = coordinator.ApplyAsync(new SettingsChange
        {
            Hotkeys = [Hotkey("A", "Alt")],
        });
        var appearanceTask = coordinator.ApplyAsync(new SettingsChange
        {
            Appearance = new AppearanceConfig { Theme = "light" },
        });

        firstSaveGate.SetResult(ConfigSaveResult.Success());
        await Task.WhenAll(hotkeysTask, appearanceTask);

        Assert.Equal("light", holder.Config!.Appearance.Theme);
        var hotkey = Assert.Single(holder.Config.Hotkeys);
        Assert.Equal("A", hotkey.Key);

        // The last published configuration is the one the store holds.
        Assert.Equal("light", store.LastSavedConfig!.Appearance.Theme);
        Assert.Single(store.LastSavedConfig.Hotkeys);
    }

    [Fact]
    public async Task OverlappingSettingsAndHotkeysApplies_AcrossEntryPoints_AreSerialized_AndBothSectionsSurvive()
    {
        // The settings window and the hotkeys/appearance editor are two distinct entry points; in the
        // composition root both resolve their ISettingsService from the same SettingsCoordinatorProvider,
        // so they share one apply gate. Without that single shared boundary each apply would build from
        // the same pre-apply snapshot and the last save would silently drop the other edit's section
        // (the "hotkeys lost" symptom class; M3-RF-615/M2-RF-922).
        var firstSaveGate = new TaskCompletionSource<ConfigSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingConfigStore();
        var saveCount = 0;
        store.SaveAsyncHandler = _ =>
        {
            var call = Interlocked.Increment(ref saveCount);
            return call == 1
                ? new ValueTask<ConfigSaveResult>(firstSaveGate.Task)
                : ValueTask.FromResult(ConfigSaveResult.Success());
        };

        var target = new RecordingPanelConfigApplyTarget();
        var holder = new Holder { Config = DefaultConfigGenerator.Create() };
        var provider = new SettingsCoordinatorProvider(
            () => holder.Config,
            store,
            new PanelConfigApplier(target),
            () => new FakeStartupRegistration(),
            updated => holder.Config = updated);

        // Each entry point obtains its boundary from the shared provider; the settings window and the
        // hotkeys/appearance editor must observe the exact same instance (one serialization boundary).
        var settingsPath = provider.Get();
        var hotkeysPath = provider.Get();
        Assert.Same(settingsPath, hotkeysPath);

        // The settings apply blocks inside the store save while the hotkeys apply starts.
        var settingsTask = settingsPath.ApplyAsync(new SettingsChange { AlwaysOnTop = true });
        var hotkeysTask = hotkeysPath.ApplyAsync(new SettingsChange { Hotkeys = [Hotkey("A", "Alt")] });

        firstSaveGate.SetResult(ConfigSaveResult.Success());
        await Task.WhenAll(settingsTask, hotkeysTask);

        Assert.True(holder.Config!.Window.AlwaysOnTop);
        Assert.Equal("A", Assert.Single(holder.Config.Hotkeys).Key);

        // The last published configuration is the one the store holds, carrying both edits.
        Assert.True(store.LastSavedConfig!.Window.AlwaysOnTop);
        Assert.Equal("A", Assert.Single(store.LastSavedConfig.Hotkeys).Key);
    }

    [Fact]
    public async Task HotkeysFollowedByAppearanceEdit_KeepHotkeysInTheFileAndReRegisterThem()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded.Config);

        var target = new RecordingPanelConfigApplyTarget();
        var holder = new Holder { Config = loaded.Config };
        var coordinator = new SettingsCoordinator(
            () => holder.Config,
            store,
            new PanelConfigApplier(target),
            new FakeStartupRegistration(),
            updated => holder.Config = updated);

        // User configures two global hotkeys (Alt+A, Alt+S), then switches the theme to light.
        var configured = await coordinator.ApplyAsync(new SettingsChange
        {
            Hotkeys = [Hotkey("A", "Alt"), Hotkey("S", "Alt")],
        });
        Assert.True(configured.Succeeded, configured.ErrorMessage);

        var themed = await coordinator.ApplyAsync(new SettingsChange
        {
            Appearance = new AppearanceConfig { Theme = "light", IconPack = "default" },
        });
        Assert.True(themed.Succeeded, themed.ErrorMessage);

        // The saved file keeps both hotkeys and the new theme.
        var text = await File.ReadAllTextAsync(temp.Config.PanelFile);
        Assert.Contains("\"theme\": \"light\"", text, StringComparison.Ordinal);
        Assert.Contains("\"key\": \"A\"", text, StringComparison.Ordinal);
        Assert.Contains("\"key\": \"S\"", text, StringComparison.Ordinal);

        // The appearance save re-registered the same two hotkeys live (ApplyHotKeys).
        Assert.Contains("hotkeys", target.Steps);
        Assert.NotNull(target.LastHotkeys);
        Assert.Equal(2, target.LastHotkeys!.Count);
        Assert.Equal("light", target.LastAppearance!.Theme);
    }

    private static PanelConfig SeededConfig()
    {
        var config = DefaultConfigGenerator.Create();
        config.Window.AlwaysOnTop = true;
        config.Startup.StartWithWindows = true;
        config.Telemetry.PollIntervalSeconds = 2.0;
        config.Monitor.Match = new MonitorIdentity { HardwareId = "DEL-A0BC" };
        config.Monitor.Fallback = MonitorFallback.Primary;
        config.Hotkeys = [Hotkey("A", "Alt")];
        config.Appearance = new AppearanceConfig { Theme = "highContrast", IconPack = "filled", AccentColor = "#112233" };
        return config;
    }

    private static HotkeyConfig Hotkey(string key, string modifier) => new()
    {
        Id = $"hk-{key}",
        Key = key,
        Modifiers = [modifier],
        Action = new MuteActionConfig(),
    };

    private static JsonConfigStore CreateStore(TempDirectory temp)
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new RecordingSink())
            .CreateLogger();

        return new JsonConfigStore(temp.Config, migrations: null, timeProvider: null, logger: logger);
    }

    private sealed class Holder
    {
        public PanelConfig? Config { get; set; }
    }
}
