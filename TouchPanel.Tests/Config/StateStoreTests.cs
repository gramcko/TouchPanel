using System.IO;
using System.Text.Json;
using Serilog;
using Serilog.Events;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// <c>state.json</c> resilience (RF-073): round-trip of runtime flags, and non-fatal regeneration
/// with a low-severity log for missing, corrupt, or stale files. Failures never throw.
/// </summary>
public class StateStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripTheCloseNotificationFlag()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);

        Assert.True(await store.SaveAsync(new RuntimeState { CloseNotificationShown = true }));

        var loaded = await store.LoadAsync();

        Assert.True(loaded.CloseNotificationShown);
        Assert.Equal(RuntimeState.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_RegeneratesDefaultsAndLogsInformation()
    {
        using var temp = new TempDirectory();
        var (store, sink) = CreateStoreWithSink(temp);

        var state = await store.LoadAsync();

        Assert.False(state.CloseNotificationShown);
        Assert.True(File.Exists(temp.Config.StateFile));
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Information);

        var persisted = JsonSerializer.Deserialize<RuntimeState>(
            File.ReadAllText(temp.Config.StateFile),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal(RuntimeState.CurrentSchemaVersion, persisted!.SchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_IsNonFatalRegeneratedAndLoggedAsWarning()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Config.StateFile, "{ not json");
        var (store, sink) = CreateStoreWithSink(temp);

        var state = await store.LoadAsync();

        Assert.False(state.CloseNotificationShown);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);

        // The regenerated file is usable on the next load.
        var reloaded = await store.LoadAsync();
        Assert.Equal(RuntimeState.CurrentSchemaVersion, reloaded.SchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_NonBooleanFlagValue_IsTreatedAsCorrupt()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.Config.StateFile,
            """{ "schemaVersion": 1, "closeNotificationShown": "yes" }""");
        var store = CreateStore(temp);

        var state = await store.LoadAsync();

        Assert.False(state.CloseNotificationShown);
    }

    [Fact]
    public async Task LoadAsync_StaleSchemaVersion_IsRegenerated()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.Config.StateFile,
            """{ "schemaVersion": 0, "closeNotificationShown": true }""");
        var store = CreateStore(temp);

        var state = await store.LoadAsync();

        Assert.False(state.CloseNotificationShown);
    }

    [Fact]
    public async Task LoadAsync_UnknownMembers_AreIgnored()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.Config.StateFile,
            """{ "schemaVersion": 1, "closeNotificationShown": true, "futureFlag": 42 }""");
        var store = CreateStore(temp);

        var state = await store.LoadAsync();

        Assert.True(state.CloseNotificationShown);
    }

    [Fact]
    public async Task SaveAsync_LockedDestination_ReturnsFalseWithoutThrowingOrCorrupting()
    {
        using var temp = new TempDirectory();
        var store = CreateStore(temp);
        Assert.True(await store.SaveAsync(new RuntimeState { CloseNotificationShown = false }));
        var originalBytes = File.ReadAllBytes(temp.Config.StateFile);

        bool saved;
        using (new FileStream(temp.Config.StateFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saved = await store.SaveAsync(new RuntimeState { CloseNotificationShown = true });
        }

        Assert.False(saved);
        Assert.Equal(originalBytes, File.ReadAllBytes(temp.Config.StateFile));
        Assert.False(File.Exists(temp.Config.StateFile + ".tmp"));

        // Retryable once the lock is gone.
        Assert.True(await store.SaveAsync(new RuntimeState { CloseNotificationShown = true }));
        Assert.True((await store.LoadAsync()).CloseNotificationShown);
    }

    private static StateStore CreateStore(TempDirectory temp) => new(temp.Config, logger: null);

    private static (StateStore Store, RecordingSink Sink) CreateStoreWithSink(TempDirectory temp)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new StateStore(temp.Config, logger), sink);
    }
}
