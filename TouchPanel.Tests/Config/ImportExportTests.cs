using System.IO;
using System.Text.Json;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.ImportExport;
using TouchPanel.Config.Json;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Checks for the T-12 import/export service (RF-083, M2-RF-831..835): export includes the schema
/// version and round-trips; import validates/migrates as a whole, writes a pre-import backup,
/// persists and applies; every failure leaves the running configuration untouched; and neither
/// operation ever logs configuration content. All I/O uses temp directories (no real profile).
/// </summary>
public sealed class ImportExportTests
{
    [Fact]
    public async Task Export_WritesValidJsonIncludingSchemaVersion()
    {
        using var temp = new TempDirectory();
        var config = DefaultConfigGenerator.Create();
        var harness = CreateService(temp, new ConfigHolder(config));

        var target = Path.Combine(temp.DirectoryPath, "exported.json");
        var result = await harness.Service.ExportAsync(target);

        Assert.True(result.Succeeded);
        Assert.Equal(target, result.Path);

        var text = await File.ReadAllTextAsync(target);
        using var document = JsonDocument.Parse(text);
        Assert.True(document.RootElement.TryGetProperty("schemaVersion", out var version));
        Assert.Equal(PanelConfig.CurrentSchemaVersion, version.GetInt32());

        var roundTripped = JsonSerializer.Deserialize<PanelConfig>(text, ConfigJson.Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(config.Pages.Count, roundTripped!.Pages.Count);
        Assert.Equal(config.ActivePageId, roundTripped.ActivePageId);
    }

    [Fact]
    public async Task Export_WithoutRunningConfig_ReportsFailureAndWritesNothing()
    {
        using var temp = new TempDirectory();
        var harness = CreateService(temp, new ConfigHolder(null));
        var target = Path.Combine(temp.DirectoryPath, "exported.json");

        var result = await harness.Service.ExportAsync(target);

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(target));
        Assert.Equal("config.export.noConfig", result.ErrorCode);
    }

    [Fact]
    public async Task Export_UnwritableTarget_ReportsFailureWithoutLeavingATempFile()
    {
        using var temp = new TempDirectory();
        var harness = CreateService(temp, new ConfigHolder(DefaultConfigGenerator.Create()));
        var directoryTarget = temp.DirectoryPath;
        var tempPath = directoryTarget + ".tmp";

        var result = await harness.Service.ExportAsync(directoryTarget);

        Assert.False(result.Succeeded);
        Assert.Equal("config.export.failed", result.ErrorCode);
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public async Task Import_ValidFile_WritesPreImportBackup_PersistsAndApplies()
    {
        using var temp = new TempDirectory();
        var initial = DefaultConfigGenerator.Create();
        var harness = CreateService(temp, new ConfigHolder(initial));

        var importPath = WriteImportFile(
            temp,
            """{"schemaVersion":2,"activePageId":"main","window":{"alwaysOnTop":true},"pages":[{"id":"main","widgets":[]}]}""");

        var result = await harness.Service.ImportAsync(importPath);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));

        // The backup captured the previously running configuration, not the imported one.
        var backup = JsonSerializer.Deserialize<PanelConfig>(
            await File.ReadAllTextAsync(result.BackupPath!),
            ConfigJson.Options);
        Assert.NotNull(backup);
        Assert.Equal(initial.Pages[0].Widgets.Count, backup!.Pages[0].Widgets.Count);

        // Persisted through the existing single-writer store, then published and applied.
        Assert.Equal(1, harness.Store.SaveCallCount);
        Assert.Same(result.Config, harness.Holder.Value);
        Assert.Same(result.Config, harness.Target.LastRebuilt);
        Assert.Contains("window-options", harness.Target.Steps);
        Assert.True(harness.Holder.Value!.Window.AlwaysOnTop);
        Assert.Contains(harness.Notifier.Notifications, n => n.Message.Contains(importPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_OlderSchema_MigratesBeforeApplying()
    {
        using var temp = new TempDirectory();
        var harness = CreateService(temp, new ConfigHolder(DefaultConfigGenerator.Create()));

        var importPath = WriteImportFile(
            temp,
            """{"schemaVersion":1,"activePageId":"main","window":{"alwaysOnTop":true},"pages":[{"id":"main","widgets":[]}]}""");

        var result = await harness.Service.ImportAsync(importPath);

        Assert.True(result.Succeeded);
        Assert.Equal(PanelConfig.CurrentSchemaVersion, result.Config!.SchemaVersion);
        Assert.Equal(PanelConfig.CurrentSchemaVersion, harness.Store.LastSavedConfig!.SchemaVersion);
        Assert.True(harness.Holder.Value!.Window.AlwaysOnTop);
        Assert.Contains("active-page", harness.Target.Steps);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("""{"schemaVersion":99,"pages":[]}""")]
    public async Task Import_InvalidOrFutureFile_ChangesNothing(string content)
    {
        using var temp = new TempDirectory();
        var initial = DefaultConfigGenerator.Create();
        var harness = CreateService(temp, new ConfigHolder(initial));

        var importPath = WriteImportFile(temp, content);
        var result = await harness.Service.ImportAsync(importPath);

        Assert.False(result.Succeeded);
        Assert.Same(initial, harness.Holder.Value);
        Assert.Equal(0, harness.Store.SaveCallCount);
        Assert.Empty(harness.Target.Steps);
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, ConfigPaths.ImportBackupFileNamePrefix + "*.json"));
    }

    [Fact]
    public async Task Import_FutureSchema_IsReportedAsFutureNotInvalid()
    {
        using var temp = new TempDirectory();
        var harness = CreateService(temp, new ConfigHolder(DefaultConfigGenerator.Create()));
        var importPath = WriteImportFile(temp, """{"schemaVersion":99,"pages":[]}""");

        var result = await harness.Service.ImportAsync(importPath);

        Assert.False(result.Succeeded);
        Assert.Equal("config.import.futureSchema", result.ErrorCode);
    }

    [Fact]
    public async Task Import_SaveFailure_LeavesRunningConfigUntouched()
    {
        using var temp = new TempDirectory();
        var initial = DefaultConfigGenerator.Create();
        var harness = CreateService(temp, new ConfigHolder(initial));
        harness.Store.SaveSucceeds = false;

        var importPath = WriteImportFile(temp, ValidImportJson());
        var result = await harness.Service.ImportAsync(importPath);

        Assert.False(result.Succeeded);
        Assert.Equal("config.import.saveFailed", result.ErrorCode);
        Assert.Same(initial, harness.Holder.Value);
        Assert.Empty(harness.Target.Steps);
    }

    [Fact]
    public async Task Import_StoreThrows_IsContainedAndNothingApplied()
    {
        using var temp = new TempDirectory();
        var initial = DefaultConfigGenerator.Create();
        var harness = CreateService(temp, new ConfigHolder(initial));
        harness.Store.SaveHandler = _ => throw new IOException("simulated store failure");

        var importPath = WriteImportFile(temp, ValidImportJson());
        var result = await harness.Service.ImportAsync(importPath);

        Assert.False(result.Succeeded);
        Assert.Same(initial, harness.Holder.Value);
        Assert.Empty(harness.Target.Steps);
    }

    [Fact]
    public async Task Import_MissingFile_ReportsReadFailure()
    {
        using var temp = new TempDirectory();
        var harness = CreateService(temp, new ConfigHolder(DefaultConfigGenerator.Create()));

        var result = await harness.Service.ImportAsync(Path.Combine(temp.DirectoryPath, "does-not-exist.json"));

        Assert.False(result.Succeeded);
        Assert.Equal("config.import.readFailed", result.ErrorCode);
        Assert.Equal(0, harness.Store.SaveCallCount);
    }

    [Fact]
    public async Task ExportThenImport_RoundTripsTheEffectiveConfiguration()
    {
        using var source = new TempDirectory();
        var config = DefaultConfigGenerator.Create();
        config.Window.AlwaysOnTop = true;
        var exportHarness = CreateService(source, new ConfigHolder(config));
        var exportedPath = Path.Combine(source.DirectoryPath, "panel-export.json");

        Assert.True((await exportHarness.Service.ExportAsync(exportedPath)).Succeeded);

        using var destination = new TempDirectory();
        var importHarness = CreateService(destination, new ConfigHolder(DefaultConfigGenerator.Create()));

        var result = await importHarness.Service.ImportAsync(exportedPath);

        Assert.True(result.Succeeded);
        Assert.True(importHarness.Holder.Value!.Window.AlwaysOnTop);
        Assert.Equal(
            JsonSerializer.Serialize(config, ConfigJson.Options),
            JsonSerializer.Serialize(importHarness.Holder.Value, ConfigJson.Options));
    }

    [Fact]
    public async Task NeitherOperation_LogsConfigurationContent()
    {
        const string secretLabel = "TOPSECRETLABEL-3f9a";
        using var temp = new TempDirectory();
        var config = DefaultConfigGenerator.Create();
        config.Pages[0].Widgets[0].Label = secretLabel;
        var (logger, sink) = TestLoggerFactory.Create();
        var harness = CreateService(temp, new ConfigHolder(config), logger);

        var exportPath = Path.Combine(temp.DirectoryPath, "exported.json");
        Assert.True((await harness.Service.ExportAsync(exportPath)).Succeeded);

        var importPath = WriteImportFile(temp, JsonSerializer.Serialize(config, ConfigJson.Options));
        Assert.True((await harness.Service.ImportAsync(importPath)).Succeeded);

        Assert.DoesNotContain(
            sink.Events,
            logEvent => logEvent.RenderMessage().Contains(secretLabel, StringComparison.Ordinal));
    }

    private static string ValidImportJson() =>
        """{"schemaVersion":2,"activePageId":"main","window":{"alwaysOnTop":true},"pages":[{"id":"main","widgets":[]}]}""";

    private static string WriteImportFile(TempDirectory temp, string content)
    {
        var path = Path.Combine(temp.DirectoryPath, "import-source.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static Harness CreateService(TempDirectory temp, ConfigHolder holder, ILogger? logger = null)
    {
        var store = new RecordingConfigStore();
        var target = new RecordingPanelConfigApplyTarget();
        var notifier = new RecordingUserNotifier();
        var applier = new PanelConfigApplier(target, logger);
        var service = new ConfigImportExport(
            store,
            applier,
            () => holder.Value,
            config => holder.Value = config,
            notifier,
            temp.Config,
            new PanelConfigCandidateReader(temp.Config, logger: logger),
            TimeProvider.System,
            logger);

        return new Harness(service, store, target, notifier, holder);
    }

    private sealed class ConfigHolder(PanelConfig? value)
    {
        public PanelConfig? Value { get; set; } = value;
    }

    private sealed record Harness(
        ConfigImportExport Service,
        RecordingConfigStore Store,
        RecordingPanelConfigApplyTarget Target,
        RecordingUserNotifier Notifier,
        ConfigHolder Holder);
}
