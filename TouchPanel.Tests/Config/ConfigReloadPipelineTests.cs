using System.IO;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Services.Notifications;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Hot-reload apply-flow behavior (T-11 part 2, M2-RF-822/M2-RF-824/M2-RF-826/M2-RF-829): a valid
/// external change is applied and published as the running configuration; an invalid or future
/// candidate keeps the running configuration with no partial apply; a byte-identical candidate is a
/// no-op; read failures never crash; and disposal stops the source.
/// </summary>
public sealed class ConfigReloadPipelineTests
{
    private static readonly string[] ExpectedSteps =
    [
        "window-options",
        "monitor-target",
        "appearance",
        "active-page",
        "telemetry-interval",
        "tracked-metrics",
        "hotkeys",
        "toggle-state",
    ];

    [Fact]
    public void ValidCandidate_IsApplied_UpdatesRunningConfig_AndRecordsContent()
    {
        using var harness = Harness.Create();
        var candidateText = """{"schemaVersion":2,"window":{"alwaysOnTop":true}}""";
        var candidateConfig = NewConfig(alwaysOnTop: true);
        harness.Reader.Candidate = PanelConfigCandidate.Valid(candidateConfig, candidateText, 2, []);

        harness.Source.Raise();

        Assert.Equal(1, harness.Pipeline.AppliedCount);
        Assert.Same(candidateConfig, harness.Holder.Value);
        Assert.Equal(ExpectedSteps, harness.Target.Steps);
        Assert.Same(candidateConfig.Window, harness.Target.LastWindow);
        Assert.Same(candidateConfig, harness.Target.LastRebuilt);

        // The applied bytes become the reference, so a repeat event is a no-op (M2-RF-823).
        Assert.Equal(ReloadDecision.Ignore, harness.Engine.EvaluateCandidate(candidateText));
    }

    [Fact]
    public void InvalidCandidate_IsRejected_RunningConfigUnchanged_NoApply_AndNotifies()
    {
        using var harness = Harness.Create();
        var running = harness.Holder.Value;
        harness.Reader.Candidate = PanelConfigCandidate.Rejected(
            PanelConfigCandidateOutcome.Invalid,
            detectedSchemaVersion: 2,
            rawContent: "{ broken",
            [ConfigDiagnostic.Error("config.reload.parseFailed", "not valid JSON")]);

        harness.Source.Raise();

        Assert.Equal(1, harness.Pipeline.RejectedCount);
        Assert.Equal(0, harness.Pipeline.AppliedCount);
        Assert.Same(running, harness.Holder.Value);
        Assert.Empty(harness.Target.Steps);

        var notification = Assert.Single(harness.Notifier.Notifications);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
        Assert.Contains("not valid", notification.Message);
    }

    [Fact]
    public void FutureSchemaCandidate_IsRejectedWithItsOwnMessage()
    {
        using var harness = Harness.Create();
        harness.Reader.Candidate = PanelConfigCandidate.Rejected(
            PanelConfigCandidateOutcome.UnsupportedFuture,
            detectedSchemaVersion: 99,
            rawContent: """{"schemaVersion":99}""",
            [ConfigDiagnostic.Error("config.reload.futureSchema", "newer than supported")]);

        harness.Source.Raise();

        Assert.Equal(1, harness.Pipeline.RejectedCount);
        Assert.Same(harness.Initial, harness.Holder.Value);
        var notification = Assert.Single(harness.Notifier.Notifications);
        Assert.Contains("newer version", notification.Message);
    }

    [Fact]
    public void ContentIdenticalToApplied_IsANoOp()
    {
        using var harness = Harness.Create();
        harness.Engine.RecordAppliedContent("unchanged-content");
        harness.Reader.Candidate = PanelConfigCandidate.Valid(
            NewConfig(alwaysOnTop: true),
            "unchanged-content",
            2,
            []);

        harness.Source.Raise();

        Assert.Equal(1, harness.Reader.ReadCount);
        Assert.Equal(0, harness.Pipeline.AppliedCount);
        Assert.Empty(harness.Target.Steps);
        Assert.Same(harness.Initial, harness.Holder.Value);
    }

    [Fact]
    public void ReadFailure_DoesNotCrash_KeepsRunningConfig_AndNotifies()
    {
        using var harness = Harness.Create();
        harness.Reader.ThrowOnRead = new IOException("simulated read failure");

        harness.Source.Raise();

        Assert.Same(harness.Initial, harness.Holder.Value);
        Assert.Empty(harness.Target.Steps);
        Assert.Single(harness.Notifier.Notifications);
    }

    [Fact]
    public void RepeatedFailures_AreThrottledToASingleNotification()
    {
        using var harness = Harness.Create(failureNotificationInterval: TimeSpan.FromMinutes(1));
        harness.Reader.CandidateFactory = () => PanelConfigCandidate.Rejected(
            PanelConfigCandidateOutcome.Invalid,
            detectedSchemaVersion: 2,
            rawContent: "{}",
            [ConfigDiagnostic.Error("config.reload.parseFailed", "bad")]);

        harness.Source.Raise();
        harness.Source.Raise();
        Assert.Single(harness.Notifier.Notifications);

        // After the throttle window elapses a fresh failure is surfaced again.
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.Source.Raise();
        Assert.Equal(2, harness.Notifier.Notifications.Count);
    }

    [Fact]
    public void Start_StartsTheSourceOnce_AndDisposeDisposesIt()
    {
        using var harness = Harness.Create(start: false);

        harness.Pipeline.Start();
        harness.Pipeline.Start();
        Assert.Equal(1, harness.Source.StartCount);

        harness.Pipeline.Dispose();
        harness.Pipeline.Dispose();
        Assert.True(harness.Source.Disposed);

        // After disposal a late event performs no read and no apply.
        harness.Source.Raise();
        Assert.Equal(0, harness.Reader.ReadCount);
    }

    private static PanelConfig NewConfig(bool alwaysOnTop) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = "main",
        Window = new WindowConfig { AlwaysOnTop = alwaysOnTop },
        Pages = [new PageConfig { Id = "main", Widgets = [] }],
    };

    private sealed class ConfigHolder(PanelConfig value)
    {
        public PanelConfig Value { get; set; } = value;
    }

    private sealed class Harness : IDisposable
    {
        private Harness(
            ConfigReloadPipeline pipeline,
            FakeConfigReloadSource source,
            FakePanelConfigCandidateReader reader,
            RecordingPanelConfigApplyTarget target,
            RecordingUserNotifier notifier,
            ReloadDecisionEngine engine,
            FakeTimeProvider clock,
            ConfigHolder holder,
            PanelConfig initial)
        {
            Pipeline = pipeline;
            Source = source;
            Reader = reader;
            Target = target;
            Notifier = notifier;
            Engine = engine;
            Clock = clock;
            Holder = holder;
            Initial = initial;
        }

        public ConfigReloadPipeline Pipeline { get; }

        public FakeConfigReloadSource Source { get; }

        public FakePanelConfigCandidateReader Reader { get; }

        public RecordingPanelConfigApplyTarget Target { get; }

        public RecordingUserNotifier Notifier { get; }

        public ReloadDecisionEngine Engine { get; }

        public FakeTimeProvider Clock { get; }

        public ConfigHolder Holder { get; }

        public PanelConfig Initial { get; }

        public static Harness Create(
            bool start = true,
            TimeSpan? failureNotificationInterval = null)
        {
            var clock = new FakeTimeProvider();
            var engine = new ReloadDecisionEngine(clock, TimeSpan.Zero, TimeSpan.Zero);
            var source = new FakeConfigReloadSource();
            var reader = new FakePanelConfigCandidateReader();
            var target = new RecordingPanelConfigApplyTarget();
            var notifier = new RecordingUserNotifier();
            var initial = NewConfig(alwaysOnTop: false);
            var holder = new ConfigHolder(initial);

            var pipeline = new ConfigReloadPipeline(
                source,
                engine,
                reader,
                new PanelConfigApplier(target, logger: null),
                () => holder.Value,
                config => holder.Value = config,
                notifier,
                clock,
                health: null,
                failureNotificationInterval,
                logger: null);

            var harness = new Harness(pipeline, source, reader, target, notifier, engine, clock, holder, initial);
            if (start)
            {
                harness.Pipeline.Start();
            }

            return harness;
        }

        public void Dispose()
        {
            Pipeline.Dispose();
            Engine.Dispose();
        }
    }
}
