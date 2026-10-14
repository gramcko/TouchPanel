using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Pure reload-decision semantics (T-11 part 1, M2-RF-830/M2-RF-821/M2-RF-823): with an injected
/// clock and no file system, the engine must ignore events for other files, coalesce events
/// (debounce), suppress app-originated writes for both the config and state stores, and treat a
/// byte-identical candidate as a no-op.
/// </summary>
public sealed class ReloadDecisionTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan SelfWrite = TimeSpan.FromSeconds(1);

    [Fact]
    public void NotifyChange_PanelFile_DebouncesAndArmsPending()
    {
        var (engine, clock) = CreateEngine();
        using var _ = engine;

        var decision = engine.NotifyChange(Change(ConfigFileChangeKind.Changed, clock, "panel.json"));

        Assert.Equal(ReloadDecision.Debounce, decision);
        Assert.True(engine.HasPendingReload);
    }

    [Theory]
    [InlineData("state.json")]
    [InlineData("panel.backup.json")]
    [InlineData("panel.json.tmp")]
    [InlineData("panel.recovery.json")]
    [InlineData("panel.corrupt-20261009-120000000.json")]
    [InlineData(null)]
    public void NotifyChange_NonEffectiveFiles_AreIgnored(string? fileName)
    {
        var (engine, clock) = CreateEngine();
        using var _ = engine;

        var decision = engine.NotifyChange(Change(ConfigFileChangeKind.Changed, clock, fileName));

        Assert.Equal(ReloadDecision.Ignore, decision);
        Assert.False(engine.HasPendingReload);
    }

    [Fact]
    public void NotifyChange_AcceptsFullPathAndIsCaseInsensitive()
    {
        var (engine, clock) = CreateEngine();
        using var _ = engine;

        var decision = engine.NotifyChange(
            new ConfigFileChange(
                ConfigFileChangeKind.Changed,
                @"C:\Users\Someone\AppData\Roaming\TouchPanel\PANEL.JSON",
                clock.UtcNow));

        Assert.Equal(ReloadDecision.Debounce, decision);
    }

    [Fact]
    public void NotifyDebounceElapsed_BeforeDue_KeepsDebouncing()
    {
        var (engine, clock) = CreateEngine();
        using var _ = engine;

        engine.NotifyChange(Change(ConfigFileChangeKind.Changed, clock, "panel.json"));
        clock.Advance(TimeSpan.FromMilliseconds(200));

        Assert.Equal(ReloadDecision.Debounce, engine.NotifyDebounceElapsed());
    }

    [Fact]
    public void NotifyDebounceElapsed_AfterDue_Reloads()
    {
        var (engine, clock) = CreateEngine();
        using var _ = engine;

        engine.NotifyChange(Change(ConfigFileChangeKind.Changed, clock, "panel.json"));
        clock.Advance(Debounce);

        Assert.Equal(ReloadDecision.Reload, engine.NotifyDebounceElapsed());
        Assert.False(engine.HasPendingReload);

        // A second elapsed without a fresh event is a no-op.
        Assert.Equal(ReloadDecision.Ignore, engine.NotifyDebounceElapsed());
    }

    [Fact]
    public void NotifyChange_CoalescesRapidEventsIntoSingleReload()
    {
        var (engine, clock) = CreateEngine();
        using var _ = engine;

        engine.NotifyChange(Change(ConfigFileChangeKind.Changed, clock, "panel.json"));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        engine.NotifyChange(Change(ConfigFileChangeKind.Changed, clock, "panel.json"));

        // The second event pushed the due time out, so the first timer tick is early.
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(ReloadDecision.Debounce, engine.NotifyDebounceElapsed());

        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(ReloadDecision.Reload, engine.NotifyDebounceElapsed());
    }

    [Fact]
    public void NotifyChange_DuringAppWrite_IsIgnored()
    {
        var (engine, clock) = CreateEngine();
        using var _ = engine;

        using (engine.BeginWrite())
        {
            Assert.Equal(ReloadDecision.Ignore, engine.NotifyChange(Change(ConfigFileChangeKind.Changed, clock, "panel.json")));
            Assert.False(engine.HasPendingReload);
        }
    }

    [Fact]
    public void NotifyChange_WithinSelfWriteWindow_IsIgnored_ThenDebouncesAfterExpiry()
    {
        var (engine, clock) = CreateEngine();
        using var _ = engine;

        using (var scope = engine.BeginWrite())
        {
            scope.RecordContent("{}");
        }

        Assert.Equal(ReloadDecision.Ignore, engine.NotifyChange(Change(ConfigFileChangeKind.Changed, clock, "panel.json")));

        clock.Advance(SelfWrite + TimeSpan.FromMilliseconds(1));
        Assert.Equal(ReloadDecision.Debounce, engine.NotifyChange(Change(ConfigFileChangeKind.Changed, clock, "panel.json")));
    }

    [Fact]
    public void EvaluateCandidate_ContentUnchanged_IsIgnored()
    {
        var (engine, _) = CreateEngine();
        using var __ = engine;

        using (var scope = engine.BeginWrite())
        {
            scope.RecordContent("""{"schemaVersion":2}""");
        }

        Assert.Equal(ReloadDecision.Ignore, engine.EvaluateCandidate("""{"schemaVersion":2}"""));
    }

    [Fact]
    public void EvaluateCandidate_DifferentContent_Reloads()
    {
        var (engine, _) = CreateEngine();
        using var __ = engine;

        using (var scope = engine.BeginWrite())
        {
            scope.RecordContent("""{"schemaVersion":2}""");
        }

        Assert.Equal(ReloadDecision.Reload, engine.EvaluateCandidate("""{"schemaVersion":2,"window":{"alwaysOnTop":true}}"""));
    }

    [Fact]
    public void RecordAppliedContent_MakesIdenticalCandidateANoOp()
    {
        var (engine, _) = CreateEngine();
        using var __ = engine;

        engine.RecordAppliedContent("external-content");

        Assert.Equal(ReloadDecision.Ignore, engine.EvaluateCandidate("external-content"));
        Assert.Equal(ReloadDecision.Reload, engine.EvaluateCandidate("other-content"));
    }

    [Fact]
    public void RecordContent_WithoutBaseline_FirstCandidateReloads()
    {
        var (engine, _) = CreateEngine();
        using var __ = engine;

        Assert.Equal(ReloadDecision.Reload, engine.EvaluateCandidate("anything"));
    }

    [Fact]
    public void Dispose_ThenAllSignalsAreIgnored()
    {
        var (engine, clock) = CreateEngine();

        engine.Dispose();

        Assert.Equal(ReloadDecision.Ignore, engine.NotifyChange(Change(ConfigFileChangeKind.Changed, clock, "panel.json")));
        Assert.Equal(ReloadDecision.Ignore, engine.NotifyDebounceElapsed());
        Assert.Equal(ReloadDecision.Ignore, engine.EvaluateCandidate("x"));
        Assert.False(engine.HasPendingReload);
    }

    [Fact]
    public void BeginWrite_AfterDispose_ReturnsUsableScopeAndDoesNotThrow()
    {
        var (engine, _) = CreateEngine();
        engine.Dispose();

        using var scope = engine.BeginWrite();
        scope.RecordContent("ignored");

        // No exception and no resurrected state.
        Assert.False(engine.HasPendingReload);
    }

    [Fact]
    public void Engine_NeverCreatesTimersOrPolls()
    {
        var (engine, clock) = CreateEngine();
        using var _ = engine;

        using (var scope = engine.BeginWrite())
        {
            scope.RecordContent("content");
            engine.NotifyChange(Change(ConfigFileChangeKind.Changed, clock, "panel.json"));
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        engine.NotifyDebounceElapsed();
        engine.EvaluateCandidate("content");

        // The pure component owns no OS/timer resources; the adapter arms the single debounce timer.
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    private static (ReloadDecisionEngine Engine, FakeTimeProvider Clock) CreateEngine()
    {
        var clock = new FakeTimeProvider();
        var engine = new ReloadDecisionEngine(clock, Debounce, SelfWrite);
        return (engine, clock);
    }

    private static ConfigFileChange Change(ConfigFileChangeKind kind, FakeTimeProvider clock, string? fileName) =>
        new(kind, fileName, clock.UtcNow);
}
