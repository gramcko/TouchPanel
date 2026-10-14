using System.IO;
using System.Text;
using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Toggle-state persistence (T-08, RF-045/M2-RF-451..455): the persisted map round-trips through
/// <c>state.json</c>, is non-fatal when absent or shape-mismatched, is written in debounced batches,
/// prunes stale keys, and never touches <c>panel.json</c>.
/// </summary>
public sealed class ToggleStateTests
{
    [Fact]
    public async Task StateStore_RoundTripsTheToggleStatesMap()
    {
        using var temp = new TempDirectory();
        var store = new StateStore(temp.Config);

        var state = new RuntimeState
        {
            CloseNotificationShown = true,
            ToggleStates = { ["main/w1"] = true, ["game/mute"] = false },
        };

        Assert.True(await store.SaveAsync(state));

        var loaded = await store.LoadAsync();

        Assert.True(loaded.CloseNotificationShown);
        Assert.Equal(2, loaded.ToggleStates.Count);
        Assert.True(loaded.ToggleStates["main/w1"]);
        Assert.False(loaded.ToggleStates["game/mute"]);
    }

    [Fact]
    public async Task StateStore_AbsentToggleStates_IsNonFatalAndYieldsAnEmptyMap()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Config.StateFile, """{ "schemaVersion": 1, "closeNotificationShown": true }""");
        var store = new StateStore(temp.Config);

        var loaded = await store.LoadAsync();

        Assert.True(loaded.CloseNotificationShown);
        Assert.Empty(loaded.ToggleStates);
    }

    [Fact]
    public async Task StateStore_ShapeMismatchedToggleStates_IsNonFatalAndRegenerated()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.Config.StateFile,
            """{ "schemaVersion": 1, "closeNotificationShown": true, "toggleStates": { "main/w1": "yes" } }""");
        var store = new StateStore(temp.Config);

        var loaded = await store.LoadAsync();

        // The bad map is discarded with the rest of the stale file and defaults are regenerated.
        Assert.Empty(loaded.ToggleStates);

        var persisted = JsonSerializer.Deserialize<RuntimeState>(
            File.ReadAllText(temp.Config.StateFile),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(persisted);
        Assert.Empty(persisted!.ToggleStates);
    }

    [Fact]
    public void CollectPersistentToggleKeys_ReturnsOnlyOptInTogglesAcrossAllPages()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Widgets =
                    [
                        new ToggleWidgetConfig { Id = "w1", PersistState = true },
                        new ToggleWidgetConfig { Id = "w2", PersistState = false },
                        new ButtonWidgetConfig { Id = "b1" },
                    ],
                },
                new PageConfig
                {
                    Id = "game",
                    Widgets = [new ToggleWidgetConfig { Id = "mute", PersistState = true }],
                },
            ],
        };

        var keys = ToggleStatePersistence.CollectPersistentToggleKeys(config);

        Assert.Equal(["main/w1", "game/mute"], keys);
    }

    [Fact]
    public async Task Persistence_SetThenFlush_PersistsAndRestoresTheState()
    {
        using var temp = new TempDirectory();
        var store = new StateStore(temp.Config);
        var state = new RuntimeState();
        var persistence = new ToggleStatePersistence(state, store.SaveAsync, new FakeTimeProvider());

        persistence.Set("main", "w1", true);
        persistence.Set("main", "w2", false);
        Assert.True(await persistence.FlushAsync());

        // A fresh load sees the values (restore path).
        var reloaded = await store.LoadAsync();
        Assert.True(reloaded.ToggleStates["main/w1"]);
        Assert.False(reloaded.ToggleStates["main/w2"]);

        var reader = new ToggleStatePersistence(reloaded, store.SaveAsync);
        Assert.True(reader.TryGet("main", "w1"));
        Assert.False(reader.TryGet("main", "w2"));
        Assert.Null(reader.TryGet("main", "missing"));
    }

    [Fact]
    public async Task Persistence_RepeatedSameValue_DoesNotAddAnotherWrite()
    {
        using var temp = new TempDirectory();
        var saveCount = 0;
        var clock = new FakeTimeProvider();
        var persistence = new ToggleStatePersistence(
            new RuntimeState(),
            (_, _) =>
            {
                saveCount++;
                return ValueTask.FromResult(true);
            },
            clock,
            TimeSpan.FromSeconds(2));

        persistence.Set("main", "w1", true);
        persistence.Set("main", "w1", true);

        Assert.Equal(1, clock.ActiveTimerCount);
        await persistence.FlushAsync();
        Assert.Equal(1, saveCount);
    }

    [Fact]
    public async Task Persistence_MultipleChangesWithinTheWindow_ProduceOneBatchedWrite()
    {
        var saveCount = 0;
        var savedStates = new List<RuntimeState>();
        var clock = new FakeTimeProvider();
        var persistence = new ToggleStatePersistence(
            new RuntimeState(),
            (snapshot, _) =>
            {
                saveCount++;
                savedStates.Add(snapshot);
                return ValueTask.FromResult(true);
            },
            clock,
            TimeSpan.FromSeconds(2));

        persistence.Set("main", "w1", true);
        persistence.Set("main", "w2", true);
        persistence.Set("main", "w3", false);

        // Still one pending debounce until the window elapses.
        Assert.Equal(0, saveCount);

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(1, saveCount);
        Assert.Equal(3, savedStates[0].ToggleStates.Count);
    }

    [Fact]
    public async Task Persistence_Prune_RemovesKeysNoLongerInTheConfiguration()
    {
        using var temp = new TempDirectory();
        var store = new StateStore(temp.Config);
        var persistence = new ToggleStatePersistence(new RuntimeState(), store.SaveAsync, new FakeTimeProvider());

        persistence.Set("main", "keep", true);
        persistence.Set("main", "gone", true);
        await persistence.FlushAsync();

        persistence.Prune(["main/keep"]);
        await persistence.FlushAsync();

        var reloaded = await store.LoadAsync();
        Assert.True(reloaded.ToggleStates.ContainsKey("main/keep"));
        Assert.False(reloaded.ToggleStates.ContainsKey("main/gone"));
    }

    [Fact]
    public async Task Persistence_Flip_DoesNotTouchPanelJson()
    {
        using var temp = new TempDirectory();
        var panelBytes = Encoding.UTF8.GetBytes("""{ "schemaVersion": 2, "activePageId": "main" }""");
        await File.WriteAllBytesAsync(temp.Config.PanelFile, panelBytes);

        var store = new StateStore(temp.Config);
        var persistence = new ToggleStatePersistence(new RuntimeState(), store.SaveAsync, new FakeTimeProvider());

        persistence.Set("main", "w1", true);
        await persistence.FlushAsync();

        Assert.Equal(panelBytes, await File.ReadAllBytesAsync(temp.Config.PanelFile));
        Assert.True(File.Exists(temp.Config.StateFile));
    }

    [Fact]
    public async Task Persistence_SaveFailure_IsNonFatalAndStaysDirtyForRetry()
    {
        var attempts = 0;
        var persistence = new ToggleStatePersistence(
            new RuntimeState(),
            (_, _) =>
            {
                attempts++;
                return ValueTask.FromResult(attempts > 1);
            },
            new FakeTimeProvider());

        persistence.Set("main", "w1", true);

        // First flush fails; the change is retained and the second flush succeeds.
        Assert.False(await persistence.FlushAsync());
        Assert.True(await persistence.FlushAsync());
        Assert.Equal(2, attempts);
        Assert.True(await persistence.FlushAsync());
        Assert.Equal(2, attempts);
    }
}
