using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Single-writer appearance persistence (T-08, M3-RF-841/M3-RF-615): an appearance edit is saved and
/// applied through the same <see cref="SettingsCoordinator"/> path as any other settings change, and
/// <see cref="SettingsCoordinator.BuildUpdated"/> preserves every section it is not explicitly
/// changing, so an appearance edit never resets hotkeys and a hotkey edit never resets appearance.
/// </summary>
public sealed class SettingsCoordinatorAppearanceTests
{
    [Fact]
    public async Task ApplyAppearance_SavesAppliesAndPreservesHotkeys()
    {
        var harness = Create();
        var original = harness.Current.Config!;
        original.Hotkeys = [Hotkey("S", "Alt")];
        var appearance = new AppearanceConfig { Theme = "light", AccentColor = "#112233", IconPack = "filled" };

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { Appearance = appearance });

        Assert.True(result.Succeeded);
        Assert.Equal(1, harness.Store.SaveCallCount);

        var saved = harness.Store.LastSavedConfig!;
        Assert.Same(appearance, saved.Appearance);
        Assert.Equal("light", saved.Appearance.Theme);
        Assert.Same(original.Hotkeys, saved.Hotkeys);
        Assert.Equal("S", Assert.Single(saved.Hotkeys).Key);
        Assert.Same(result.Config, harness.Current.Config);
    }

    [Fact]
    public async Task ApplyHotkeys_PreservesAppearance()
    {
        var harness = Create();
        var original = harness.Current.Config!;
        original.Appearance = new AppearanceConfig { Theme = "highContrast", AccentColor = "#445566", IconPack = "filled" };

        var result = await harness.Coordinator.ApplyAsync(
            new SettingsChange { Hotkeys = [Hotkey("A", "Ctrl")] });

        Assert.True(result.Succeeded);
        var saved = harness.Store.LastSavedConfig!;
        Assert.Same(original.Appearance, saved.Appearance);
        Assert.Equal("highContrast", saved.Appearance.Theme);
        Assert.Equal("#445566", saved.Appearance.AccentColor);
        Assert.Equal("filled", saved.Appearance.IconPack);
    }

    [Fact]
    public async Task ApplyUnrelatedChange_PreservesAppearance()
    {
        var harness = Create();
        var original = harness.Current.Config!;
        original.Appearance = new AppearanceConfig { Theme = "light" };

        var result = await harness.Coordinator.ApplyAsync(new SettingsChange { AlwaysOnTop = true });

        Assert.True(result.Succeeded);
        var saved = harness.Store.LastSavedConfig!;
        Assert.Same(original.Appearance, saved.Appearance);
        Assert.Equal("light", saved.Appearance.Theme);
        Assert.True(saved.Window.AlwaysOnTop);
    }

    [Fact]
    public async Task ApplyAppearance_SaveFailure_ChangesNothingAndNeverReachesTheApplyHook()
    {
        var harness = Create();
        var original = harness.Current.Config!;
        original.Appearance = new AppearanceConfig { Theme = "dark" };
        harness.Store.SaveSucceeds = false;

        var result = await harness.Coordinator.ApplyAsync(
            new SettingsChange { Appearance = new AppearanceConfig { Theme = "light" } });

        Assert.False(result.Succeeded);
        Assert.Equal("settings.saveFailed", result.ErrorCode);
        Assert.Empty(harness.Target.Steps);
        Assert.Equal("dark", harness.Current.Config!.Appearance.Theme);
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
