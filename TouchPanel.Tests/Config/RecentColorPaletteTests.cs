using System.IO;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Most-recently-used colour list (user-requested colour picker): move-to-front, case-insensitive
/// de-duplication, oldest-dropped fixed size, default seeding, tolerant persistence in
/// <c>state.json</c>, and the debounced save. All file access is isolated to a temp directory.
/// </summary>
public sealed class RecentColorPaletteTests
{
    [Fact]
    public void RecentColorPalette_SeedsTheStandardPaletteInOrder()
    {
        var palette = new RecentColorPalette();

        Assert.Equal(RecentColorPalette.Capacity, palette.Colors.Count);
        Assert.Equal(ColorPalette.Standard.Select(swatch => swatch.Hex), palette.HexColors);
        Assert.Equal("Black", palette.Colors[0].Name);
    }

    [Fact]
    public void Record_MovesTheColourToTheFront()
    {
        var palette = new RecentColorPalette();

        palette.Record("#FF4D4D");
        palette.Record("#14B8A6");

        Assert.Equal("#14B8A6", palette.HexColors[0]);
        Assert.Equal("#FF4D4D", palette.HexColors[1]);
        Assert.Equal(RecentColorPalette.Capacity, palette.HexColors.Count);
    }

    [Fact]
    public void Record_DeduplicatesCaseInsensitivelyAndNormalizes()
    {
        var palette = new RecentColorPalette();

        palette.Record("#ff4d4d");
        palette.Record("#FF4D4D");
        palette.Record("#abc");

        Assert.Equal("#AABBCC", palette.HexColors[0]);
        Assert.Equal("#FF4D4D", palette.HexColors[1]);
        Assert.Single(palette.HexColors, hex => string.Equals(hex, "#FF4D4D", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Record_DropsTheOldestWhenTheListIsFull()
    {
        var palette = new RecentColorPalette();

        for (var index = 0; index < 30; index++)
        {
            palette.Record($"#{index:X2}0000");
        }

        Assert.Equal(RecentColorPalette.Capacity, palette.HexColors.Count);
        Assert.Equal("#1D0000", palette.HexColors[0]);
        Assert.DoesNotContain("#000000", palette.HexColors);
        Assert.DoesNotContain("#050000", palette.HexColors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-colour")]
    public void Record_IgnoresBlankOrMalformedValues(string? value)
    {
        var palette = new RecentColorPalette();
        var before = palette.HexColors.ToArray();

        palette.Record(value);

        Assert.Equal(before, palette.HexColors);
    }

    [Fact]
    public void Normalize_FallsBackToDefaultsForMissingEmptyOrInvalidLists()
    {
        Assert.Equal(RecentColorPalette.Defaults, RecentColorPalette.Normalize(null));
        Assert.Equal(RecentColorPalette.Defaults, RecentColorPalette.Normalize([]));
        Assert.Equal(RecentColorPalette.Defaults, RecentColorPalette.Normalize(["bad", "#GG0000", "red"]));
    }

    [Fact]
    public void Normalize_DeduplicatesAndCapsAnOversizedPersistedList()
    {
        var seed = Enumerable.Range(0, 40).Select(index => $"#{index:X2}00FF")
            .Concat(["#FF0000", "#ff0000"])
            .ToArray();

        var normalized = RecentColorPalette.Normalize(seed);

        Assert.Equal(RecentColorPalette.Capacity, normalized.Count);
        Assert.Equal(seed[0].ToUpperInvariant(), normalized[0]);
        Assert.Equal("#1700FF", normalized[^1]);
        Assert.DoesNotContain("#FF0000", normalized);
    }

    [Fact]
    public void RuntimeState_RecentColors_IsTolerantAndAdditive()
    {
        var defaults = new RuntimeState();
        Assert.Equal(ColorPalette.Standard.Select(swatch => swatch.Hex), defaults.RecentColors);

        defaults.RecentColors = null!;
        Assert.Equal(RecentColorPalette.Defaults, defaults.RecentColors);

        defaults.RecentColors = ["#ff0000", "#FF0000", "bad"];
        Assert.Equal(["#FF0000"], defaults.RecentColors);
    }

    [Fact]
    public async Task RecentColorPersistence_RoundTripsThroughStateJson()
    {
        using var temp = new TempDirectory();
        var store = new StateStore(temp.Config);
        var state = new RuntimeState();
        using var persistence = new RecentColorPersistence(state, store.SaveAsync);

        persistence.Record("#14B8A6");
        persistence.Record("#FF4D4D");
        Assert.True(await persistence.FlushAsync());

        var loaded = await store.LoadAsync();
        Assert.Equal("#FF4D4D", loaded.RecentColors[0]);
        Assert.Equal("#14B8A6", loaded.RecentColors[1]);
        Assert.Equal(RecentColorPalette.Capacity, loaded.RecentColors.Count);
    }

    [Fact]
    public async Task StateJson_MissingOrCorruptRecentColours_FallsBackToDefaults()
    {
        using var temp = new TempDirectory();
        var store = new StateStore(temp.Config);

        File.WriteAllText(temp.Config.StateFile, """{ "schemaVersion": 1 }""");
        var missing = await store.LoadAsync();
        Assert.Equal(RecentColorPalette.Defaults, missing.RecentColors);

        File.WriteAllText(
            temp.Config.StateFile,
            """{ "schemaVersion": 1, "recentColors": ["bad", "#GG0000", 42] }""");
        var corrupt = await store.LoadAsync();
        Assert.Equal(RecentColorPalette.Defaults, corrupt.RecentColors);
    }

    [Fact]
    public async Task StateJson_OversizedOrDuplicatedRecentColours_IsNormalizedOnLoad()
    {
        using var temp = new TempDirectory();
        var store = new StateStore(temp.Config);
        var colors = new List<string> { "\"#ff0000\"", "\"#FF0000\"" };
        colors.AddRange(Enumerable.Range(0, 30).Select(index => $"\"#{index:X2}1200\""));

        File.WriteAllText(
            temp.Config.StateFile,
            $$"""{ "schemaVersion": 1, "recentColors": [{{string.Join(",", colors)}}] }""");

        var loaded = await store.LoadAsync();

        Assert.Equal(RecentColorPalette.Capacity, loaded.RecentColors.Count);
        Assert.Single(loaded.RecentColors, hex => string.Equals(hex, "#FF0000", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RecentColorPersistence_DebouncesAndKeepsEveryRuntimeFlag()
    {
        var time = new FakeTimeProvider();
        var state = new RuntimeState
        {
            CloseNotificationShown = true,
            ToggleStates = { ["main/t1"] = true },
            RecentColors = ["#ABCDEF"],
        };

        var saved = new TaskCompletionSource<RuntimeState>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var persistence = new RecentColorPersistence(
            state,
            (snapshot, _) =>
            {
                saved.TrySetResult(snapshot);
                return ValueTask.FromResult(true);
            },
            time,
            TimeSpan.FromSeconds(2));

        persistence.Record("#FF4D4D");
        Assert.False(saved.Task.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(2));
        var snapshot = await saved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("#FF4D4D", snapshot.RecentColors[0]);
        Assert.True(snapshot.CloseNotificationShown);
        Assert.True(snapshot.ToggleStates["main/t1"]);
    }

    [Fact]
    public async Task TogglePersistence_SaveDoesNotDropRecentColours()
    {
        using var temp = new TempDirectory();
        var store = new StateStore(temp.Config);
        var state = new RuntimeState { RecentColors = ["#FF4D4D"] };
        using var toggles = new ToggleStatePersistence(state, store.SaveAsync);

        toggles.Set("main", "t1", true);
        Assert.True(await toggles.FlushAsync());

        var loaded = await store.LoadAsync();
        Assert.Equal("#FF4D4D", loaded.RecentColors[0]);
        Assert.True(loaded.ToggleStates["main/t1"]);
    }
}
