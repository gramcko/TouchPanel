using System.IO;
using System.Text.Json;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Migrations;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Covers the four RF-075 load states end to end against a temp directory: current valid,
/// older supported (migration), invalid/corrupt (recovery + preservation), and unsupported
/// future (no interpretation, no overwrite).
/// </summary>
public class ConfigStoreLoadStateTests
{
    // --- RF-075.1 current valid ---------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_CurrentValid_LoadsNormallyAndLeavesTheFileUntouched()
    {
        using var temp = new TempDirectory();
        var config = DefaultConfigGenerator.Create();
        config.Window.AlwaysOnTop = true;
        var originalBytes = WritePanel(temp, Serialize(config));
        var (store, _) = CreateStore(temp);

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Current, result.State);
        Assert.Equal(PanelConfig.CurrentSchemaVersion, result.DetectedSchemaVersion);
        Assert.NotNull(result.Config);
        Assert.True(result.Config.Window.AlwaysOnTop);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == ConfigDiagnosticSeverity.Error);
        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.PanelFile));
    }

    [Fact]
    public async Task LoadAsync_CurrentValid_ReportsEntryLevelFixesAsDiagnostics()
    {
        using var temp = new TempDirectory();
        var config = DefaultConfigGenerator.Create();
        config.ActivePageId = "does-not-exist";
        WritePanel(temp, Serialize(config));
        var (store, _) = CreateStore(temp);

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Current, result.State);
        Assert.Equal(DefaultConfigGenerator.DefaultPageId, result.Config!.ActivePageId);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.activePage.substituted");
    }

    [Fact]
    public async Task LoadAsync_FirstRun_GeneratesAndSavesDefaults()
    {
        using var temp = new TempDirectory();
        var (store, _) = CreateStore(temp);

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Current, result.State);
        Assert.NotNull(result.Config);
        Assert.True(File.Exists(temp.Config.PanelFile));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.missing");

        var persisted = Deserialize(File.ReadAllText(temp.Config.PanelFile));
        Assert.Equal(PanelConfig.CurrentSchemaVersion, persisted.SchemaVersion);
        Assert.Equal(DefaultConfigGenerator.DefaultPageId, persisted.ActivePageId);
        Assert.NotEmpty(persisted.Pages[0].Widgets);
    }

    // --- RF-075.2 older supported -------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_OlderSupported_MigratesValidatesBacksUpAndSaves()
    {
        using var temp = new TempDirectory();
        var v0 = CreateV0Config(x: 10);
        WritePanel(temp, Serialize(v0));
        var (store, _) = CreateStore(temp, new MigrationRunner([new SyntheticV0ToV1Migration(), new V1ToV2Migration(), new V2ToV3Migration(), new V3ToV4Migration()]));

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.OlderSupported, result.State);
        Assert.Equal(0, result.DetectedSchemaVersion);
        Assert.NotNull(result.Config);
        Assert.Equal(10 + SyntheticV0ToV1Migration.MarkerOffset, result.Config.Pages[0].Widgets[0].X);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.migration.applied");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.migrated.saved");

        var persisted = Deserialize(File.ReadAllText(temp.Config.PanelFile));
        Assert.Equal(PanelConfig.CurrentSchemaVersion, persisted.SchemaVersion);
        Assert.Equal(10 + SyntheticV0ToV1Migration.MarkerOffset, persisted.Pages[0].Widgets[0].X);

        var backup = Deserialize(File.ReadAllText(temp.Config.BackupFile));
        Assert.Equal(0, backup.SchemaVersion);
        Assert.Equal(10, backup.Pages[0].Widgets[0].X);
    }

    [Fact]
    public async Task LoadAsync_OlderSupported_WithoutMigrationStep_FollowsTheInvalidPath()
    {
        using var temp = new TempDirectory();
        var originalBytes = WritePanel(temp, Serialize(CreateV0Config(x: 10)));
        var (store, _) = CreateStore(temp); // production v1 baseline: no v0 step exists

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Invalid, result.State);
        Assert.Equal(0, result.DetectedSchemaVersion);
        Assert.NotNull(result.Config);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.migration.failed");

        // RF-076: the source file is never destructively overwritten; it is preserved for diagnosis.
        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.PanelFile));
        var preserved = Assert.Single(Directory.GetFiles(temp.DirectoryPath, ConfigPaths.CorruptFileSearchPattern));
        Assert.Equal(originalBytes, File.ReadAllBytes(preserved));
    }

    // --- RF-075.3 invalid/corrupt -------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_InvalidJson_PreservesSourceAndRecoversFromBackup()
    {
        using var temp = new TempDirectory();
        var backupConfig = DefaultConfigGenerator.Create();
        backupConfig.Window.AlwaysOnTop = true;
        WriteBackup(temp, Serialize(backupConfig));

        const string garbage = "{ this is not json";
        var originalBytes = WritePanel(temp, garbage);
        var (store, _) = CreateStore(temp);

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Invalid, result.State);
        Assert.NotNull(result.Config);
        Assert.True(result.Config.Window.AlwaysOnTop);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.recovered.backup");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.parse.failed");

        // The bad file is neither overwritten nor deleted; a preservation copy carries its bytes.
        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.PanelFile));
        var preserved = Assert.Single(Directory.GetFiles(temp.DirectoryPath, ConfigPaths.CorruptFileSearchPattern));
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(garbage), File.ReadAllBytes(preserved));
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_WithoutBackup_RecoversGeneratedDefaults()
    {
        using var temp = new TempDirectory();
        WritePanel(temp, "{ broken");
        var (store, _) = CreateStore(temp);

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Invalid, result.State);
        Assert.NotNull(result.Config);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.recovered.defaults");
        Assert.Single(Directory.GetFiles(temp.DirectoryPath, ConfigPaths.CorruptFileSearchPattern));
    }

    [Theory]
    [InlineData("""{ "pages": [] }""", "config.schema.missing")]
    [InlineData("[1, 2, 3]", "config.root.invalid")]
    [InlineData("""{ "schemaVersion": 1, "pages": "not-an-array" }""", "config.structure.invalid")]
    [InlineData("""{ "schemaVersion": 1, "pages": [ { "id": "main", "widgets": [ { "id": "w" } ] } ] }""", "config.structure.invalid")]
    public async Task LoadAsync_FileLevelProblems_RouteToTheInvalidPath(string json, string expectedCode)
    {
        using var temp = new TempDirectory();
        WritePanel(temp, json);
        var (store, _) = CreateStore(temp);

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Invalid, result.State);
        Assert.NotNull(result.Config);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }

    [Fact]
    public async Task LoadAsync_StructurallyInvalid_KeepsTheDetectedSchemaVersion()
    {
        using var temp = new TempDirectory();
        WritePanel(temp, """{ "schemaVersion": 1, "pages": "not-an-array" }""");
        var (store, _) = CreateStore(temp);

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Invalid, result.State);
        Assert.Equal(1, result.DetectedSchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_OlderBackup_IsMigratedWhenRecovering()
    {
        using var temp = new TempDirectory();
        WriteBackup(temp, Serialize(CreateV0Config(x: 20)));
        WritePanel(temp, "{ corrupt");
        var (store, _) = CreateStore(temp, new MigrationRunner([new SyntheticV0ToV1Migration(), new V1ToV2Migration(), new V2ToV3Migration(), new V3ToV4Migration()]));

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Invalid, result.State);
        Assert.NotNull(result.Config);
        Assert.Equal(20 + SyntheticV0ToV1Migration.MarkerOffset, result.Config.Pages[0].Widgets[0].X);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.recovered.backup");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.migration.applied");
    }

    // --- RF-075.4 unsupported future ----------------------------------------------------------

    [Fact]
    public async Task LoadAsync_UnsupportedFuture_IsNotInterpretedNotOverwrittenAndPreserved()
    {
        using var temp = new TempDirectory();
        const string future = """
            {
              "schemaVersion": 99,
              "activePageId": "future-page",
              "pages": [ { "id": "future-page", "widgets": [ { "type": "holo", "id": "x" } ] } ]
            }
            """;
        var originalBytes = WritePanel(temp, future);
        var (store, _) = CreateStore(temp);

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.UnsupportedFuture, result.State);
        Assert.Equal(99, result.DetectedSchemaVersion);
        Assert.NotNull(result.Config);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.schema.future");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.recovered.defaults");

        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.PanelFile));
        var preserved = Assert.Single(Directory.GetFiles(temp.DirectoryPath, ConfigPaths.CorruptFileSearchPattern));
        Assert.Equal(originalBytes, File.ReadAllBytes(preserved));
    }

    [Fact]
    public async Task LoadAsync_UnsupportedFuture_UsesTheBackupWhenAvailable()
    {
        using var temp = new TempDirectory();
        var backupConfig = DefaultConfigGenerator.Create();
        backupConfig.Window.AlwaysOnTop = true;
        WriteBackup(temp, Serialize(backupConfig));
        WritePanel(temp, """{ "schemaVersion": 99 }""");
        var (store, _) = CreateStore(temp);

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.UnsupportedFuture, result.State);
        Assert.True(result.Config!.Window.AlwaysOnTop);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "config.recovered.backup");
    }

    [Fact]
    public async Task LoadAsync_FutureFile_LaterSaveIsRefusedAndTheFileStaysIntact()
    {
        using var temp = new TempDirectory();
        var originalBytes = WritePanel(temp, """{ "schemaVersion": 99, "future": true }""");
        var (store, _) = CreateStore(temp);

        var load = await store.LoadAsync();
        var save = await store.SaveAsync(load.Config!);

        // RF-075.4: the future source must not be overwritten by a later save.
        Assert.False(save.Succeeded);
        Assert.Contains(save.Diagnostics, diagnostic => diagnostic.Code == "config.save.protectedFutureSource");
        Assert.False(File.Exists(temp.Config.BackupFile));
        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.PanelFile));
    }

    // --- RF-077: first-run and migration save failures are surfaced ---------------------------

    [Fact]
    public async Task LoadAsync_FirstRun_WhenTheDefaultSaveFails_ReportsASaveFailure()
    {
        using var temp = new TempDirectory();

        // A directory in place of the temp file makes the durable write fail (simulated I/O error).
        Directory.CreateDirectory(temp.Config.PanelFile + ".tmp");
        var (store, _) = CreateStore(temp);

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.Current, result.State);
        Assert.NotNull(result.Config);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == ConfigDiagnosticSeverity.Error && diagnostic.Code == "config.save.failed");
        Assert.False(File.Exists(temp.Config.PanelFile));
    }

    [Fact]
    public async Task LoadAsync_OlderSupported_WhenTheMigratedSaveFails_ReportsASaveFailureAndPreservesTheSource()
    {
        using var temp = new TempDirectory();
        var originalBytes = WritePanel(temp, Serialize(CreateV0Config(x: 10)));
        Directory.CreateDirectory(temp.Config.PanelFile + ".tmp");
        var (store, _) = CreateStore(temp, new MigrationRunner([new SyntheticV0ToV1Migration(), new V1ToV2Migration(), new V2ToV3Migration(), new V3ToV4Migration()]));

        var result = await store.LoadAsync();

        Assert.Equal(ConfigState.OlderSupported, result.State);
        Assert.NotNull(result.Config);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == ConfigDiagnosticSeverity.Error && diagnostic.Code == "config.save.failed");
        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.PanelFile));
    }

    // --- every state yields a usable configuration --------------------------------------------

    [Fact]
    public async Task LoadAsync_EveryState_ProducesAUsableConfiguration()
    {
        var scenarios = new (string Name, Func<TempDirectory, MigrationRunner, Task<ConfigLoadResult>> Arrange, ConfigState Expected)[]
        {
            ("current", async (temp, _) =>
            {
                WritePanel(temp, Serialize(DefaultConfigGenerator.Create()));
                return await CreateStore(temp).Store.LoadAsync();
            }, ConfigState.Current),

            ("older", async (temp, migrations) =>
            {
                WritePanel(temp, Serialize(CreateV0Config(x: 5)));
                return await CreateStore(temp, migrations).Store.LoadAsync();
            }, ConfigState.OlderSupported),

            ("invalid", async (temp, _) =>
            {
                WritePanel(temp, "{ nope");
                return await CreateStore(temp).Store.LoadAsync();
            }, ConfigState.Invalid),

            ("future", async (temp, _) =>
            {
                WritePanel(temp, """{ "schemaVersion": 99 }""");
                return await CreateStore(temp).Store.LoadAsync();
            }, ConfigState.UnsupportedFuture),
        };

        foreach (var scenario in scenarios)
        {
            using var temp = new TempDirectory();
            var result = await scenario.Arrange(temp, new MigrationRunner([new SyntheticV0ToV1Migration(), new V1ToV2Migration(), new V2ToV3Migration(), new V3ToV4Migration()]));

            Assert.Equal(scenario.Expected, result.State);
            Assert.NotNull(result.Config);
            Assert.NotNull(result.Diagnostics);
        }
    }

    private static (JsonConfigStore Store, RecordingSink Sink) CreateStore(
        TempDirectory temp,
        MigrationRunner? migrations = null)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new JsonConfigStore(temp.Config, migrations, timeProvider: null, logger), sink);
    }

    private static byte[] WritePanel(TempDirectory temp, string json)
    {
        File.WriteAllText(temp.Config.PanelFile, json);
        return File.ReadAllBytes(temp.Config.PanelFile);
    }

    private static void WriteBackup(TempDirectory temp, string json) =>
        File.WriteAllText(temp.Config.BackupFile, json);

    private static string Serialize(PanelConfig config) =>
        JsonSerializer.Serialize(config, ConfigJson.Options);

    private static PanelConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

    private static PanelConfig CreateV0Config(double x) =>
        new()
        {
            SchemaVersion = 0,
            ActivePageId = "main",
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Widgets =
                    [
                        new LabelWidgetConfig { Id = "w", X = x, Y = 10, Width = 100, Height = 50 },
                    ],
                },
            ],
        };
}
