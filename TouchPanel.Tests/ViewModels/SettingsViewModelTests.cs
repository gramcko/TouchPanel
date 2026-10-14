using System.IO;
using TouchPanel.Config;
using TouchPanel.Config.ImportExport;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Settings-window presentation state (T-17, RF-092/M2-RF-921..925): initial values and diagnostics
/// come from the running configuration, each accepted change is applied through the settings
/// boundary (apply intent), out-of-range interval input is blocked inline and never reaches the
/// boundary, and import/export are wired through the existing service and the picker seam. No WPF
/// and no real profile (M2-RNF-124).
/// </summary>
public sealed class SettingsViewModelTests
{
    [Fact]
    public void InitialValues_ReflectTheRunningConfiguration()
    {
        var config = DefaultConfigGenerator.Create();
        config.Window.AlwaysOnTop = true;
        config.Startup.StartWithWindows = true;
        config.Telemetry.PollIntervalSeconds = 2.0;
        config.Monitor.Match = new MonitorIdentity { HardwareId = "DEL-A0BC", FriendlyName = "DELL U2415" };

        var vm = Create(config, out _);

        Assert.True(vm.AlwaysOnTop);
        Assert.True(vm.StartWithWindows);
        Assert.Equal(2.0, vm.PollIntervalSeconds);
        Assert.Equal(PanelConfig.CurrentSchemaVersion.ToString(), vm.SchemaVersionText);
        Assert.Contains("DEL-A0BC", vm.CurrentMonitorText, StringComparison.Ordinal);
        Assert.Contains("DELL U2415", vm.CurrentMonitorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetAlwaysOnTop_AppliesTheIntentAndUpdatesTheReflectedValue()
    {
        var vm = Create(DefaultConfigGenerator.Create(), out var settings);

        var applied = await vm.SetAlwaysOnTopAsync(true);

        Assert.True(applied);
        var change = Assert.Single(settings.Changes);
        Assert.True(change.AlwaysOnTop);
        Assert.True(vm.AlwaysOnTop);
    }

    [Fact]
    public async Task SetStartWithWindows_AppliesTheIntent()
    {
        var vm = Create(DefaultConfigGenerator.Create(), out var settings);

        var applied = await vm.SetStartWithWindowsAsync(true);

        Assert.True(applied);
        var change = Assert.Single(settings.Changes);
        Assert.True(change.StartWithWindows);
        Assert.True(vm.StartWithWindows);
    }

    [Fact]
    public async Task ApplyInterval_OutOfRange_IsBlockedInlineAndNeverReachesTheBoundary()
    {
        var vm = Create(DefaultConfigGenerator.Create(), out var settings);
        vm.PollIntervalSeconds = 99;

        var applied = await vm.ApplyIntervalAsync();

        Assert.False(applied);
        Assert.True(vm.HasIntervalError);
        Assert.True(vm.StatusIsError);
        Assert.Empty(settings.Changes);
    }

    [Fact]
    public async Task ApplyInterval_NonFinite_IsBlockedInline()
    {
        var vm = Create(DefaultConfigGenerator.Create(), out var settings);
        vm.PollIntervalSeconds = double.NaN;

        var applied = await vm.ApplyIntervalAsync();

        Assert.False(applied);
        Assert.True(vm.HasIntervalError);
        Assert.Empty(settings.Changes);
    }

    [Fact]
    public async Task ApplyInterval_Valid_AppliesThroughTheBoundary()
    {
        var vm = Create(DefaultConfigGenerator.Create(), out var settings);
        vm.PollIntervalSeconds = 3.5;

        var applied = await vm.ApplyIntervalAsync();

        Assert.True(applied);
        var change = Assert.Single(settings.Changes);
        Assert.Equal(3.5, change.PollIntervalSeconds);
        Assert.False(vm.HasIntervalError);
    }

    [Fact]
    public async Task ApplyFailure_SurfacesTheErrorMessage()
    {
        var vm = Create(DefaultConfigGenerator.Create(), out var settings);
        settings.Result = SettingsApplyResult.Failure("settings.saveFailed", "The settings could not be saved.");

        var applied = await vm.SetAlwaysOnTopAsync(true);

        Assert.False(applied);
        Assert.True(vm.StatusIsError);
        Assert.Equal("The settings could not be saved.", vm.StatusMessage);
        Assert.False(vm.AlwaysOnTop);
    }

    [Fact]
    public void CanImportExport_IsFalseWhenTheServiceIsUnavailable()
    {
        var vm = new SettingsViewModel(
            new FakeSettingsService(),
            () => DefaultConfigGenerator.Create(),
            importExport: null,
            new FakeConfigFilePicker(),
            "panel.json");

        Assert.False(vm.CanImportExport);
    }

    [Fact]
    public async Task Export_Canceled_ReportsAndWritesNothing()
    {
        using var temp = new TempDirectory();
        var vm = CreateWithImportExport(temp, out var settings, out var picker);
        picker.ExportPath = null;

        var exported = await vm.ExportAsync();

        Assert.False(exported);
        Assert.Contains("canceled", vm.StatusMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, picker.ExportPickCount);
        Assert.Empty(settings.Changes);
    }

    [Fact]
    public async Task Export_InvokesTheServiceWithThePickedPathAndWritesTheFile()
    {
        using var temp = new TempDirectory();
        var vm = CreateWithImportExport(temp, out _, out var picker);
        var target = Path.Combine(temp.DirectoryPath, "exported.json");
        picker.ExportPath = target;

        var exported = await vm.ExportAsync();

        Assert.True(exported);
        Assert.True(File.Exists(target));
        Assert.Contains(target, vm.StatusMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_InvokesTheServiceAndRefreshesTheReflectedValues()
    {
        using var temp = new TempDirectory();
        var vm = CreateWithImportExport(temp, out _, out var picker);
        var source = Path.Combine(temp.DirectoryPath, "import-source.json");
        await File.WriteAllTextAsync(
            source,
            """{"schemaVersion":2,"activePageId":"main","window":{"alwaysOnTop":true},"pages":[{"id":"main","widgets":[]}]}""");
        picker.ImportPath = source;

        var imported = await vm.ImportAsync();

        Assert.True(imported);
        Assert.True(vm.AlwaysOnTop);
        Assert.Contains(source, vm.StatusMessage!, StringComparison.Ordinal);
    }

    private static SettingsViewModel Create(PanelConfig config, out FakeSettingsService settings)
    {
        settings = new FakeSettingsService();
        return new SettingsViewModel(
            settings,
            () => config,
            importExport: null,
            new FakeConfigFilePicker(),
            "panel.json");
    }

    private static SettingsViewModel CreateWithImportExport(
        TempDirectory temp,
        out FakeSettingsService settings,
        out FakeConfigFilePicker picker)
    {
        settings = new FakeSettingsService();
        picker = new FakeConfigFilePicker();

        var config = DefaultConfigGenerator.Create();
        var holder = new Holder(config);
        var store = new RecordingConfigStore();
        var target = new RecordingPanelConfigApplyTarget();
        var applier = new PanelConfigApplier(target);
        var notifier = new RecordingUserNotifier();
        var importExport = new ConfigImportExport(
            store,
            applier,
            () => holder.Config,
            updated => holder.Config = updated,
            notifier,
            temp.Config,
            new PanelConfigCandidateReader(temp.Config),
            TimeProvider.System);

        return new SettingsViewModel(
            settings,
            () => holder.Config,
            importExport,
            picker,
            temp.Config.PanelFile);
    }

    private sealed class Holder(PanelConfig? config)
    {
        public PanelConfig? Config { get; set; } = config;
    }
}
