using System.IO;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Temp-directory integration checks for the full hot-reload apply flow (T-11 part 2): the real
/// reader reads the whole file, migrates/validates it, and the shared applier publishes it as the
/// running configuration; an invalid edit is rejected with no partial apply; an older external file
/// migrates and applies. The watcher itself is covered by
/// <see cref="ConfigReloadControllerTests"/>; here a deterministic source raises the requests.
/// </summary>
public sealed class ConfigReloadPipelineIntegrationTests
{
    [Fact]
    public async Task ValidExternalEdit_AppliesAndUpdatesRunningState()
    {
        using var temp = new TempDirectory();
        var initial = DefaultConfigGenerator.Create();
        var holder = new Holder(initial);

        await File.WriteAllTextAsync(
            temp.Config.PanelFile,
            """{"schemaVersion":2,"activePageId":"main","window":{"alwaysOnTop":true},"pages":[{"id":"main","widgets":[]}]}""");

        using var harness = CreateHarness(temp, holder);
        harness.Source.Raise();

        await WaitUntilAsync(() => harness.Pipeline.AppliedCount == 1);
        Assert.True(holder.Value.Window.AlwaysOnTop);
        Assert.Same(holder.Value, harness.Target.LastRebuilt);
        Assert.Contains("window-options", harness.Target.Steps);
    }

    [Fact]
    public async Task InvalidExternalEdit_IsRejected_AndRunningStateIsUnchanged()
    {
        using var temp = new TempDirectory();
        var initial = DefaultConfigGenerator.Create();
        var holder = new Holder(initial);

        await File.WriteAllTextAsync(temp.Config.PanelFile, "{ this is not json");
        var originalBytes = await File.ReadAllTextAsync(temp.Config.PanelFile);

        using var harness = CreateHarness(temp, holder);
        harness.Source.Raise();

        await WaitUntilAsync(() => harness.Pipeline.RejectedCount == 1);
        Assert.Same(initial, holder.Value);
        Assert.Empty(harness.Target.Steps);
        Assert.Single(harness.Notifier.Notifications);

        // The reload path never rewrites or preserves the user's file (M2-RF-826).
        Assert.Equal(originalBytes, await File.ReadAllTextAsync(temp.Config.PanelFile));
    }

    [Fact]
    public async Task OlderExternalFile_IsMigratedAndApplied()
    {
        using var temp = new TempDirectory();
        var initial = DefaultConfigGenerator.Create();
        var holder = new Holder(initial);

        var v1 = """{"schemaVersion":1,"activePageId":"main","window":{"alwaysOnTop":true},"pages":[{"id":"main","widgets":[]}]}""";
        await File.WriteAllTextAsync(temp.Config.PanelFile, v1);

        using var harness = CreateHarness(temp, holder);
        harness.Source.Raise();

        await WaitUntilAsync(() => harness.Pipeline.AppliedCount == 1);
        Assert.Equal(PanelConfig.CurrentSchemaVersion, holder.Value.SchemaVersion);
        Assert.True(holder.Value.Window.AlwaysOnTop);

        // The migration is in-memory only; the watched source is untouched (M2-RF-825).
        Assert.Equal(v1, await File.ReadAllTextAsync(temp.Config.PanelFile));
    }

    private static Harness CreateHarness(TempDirectory temp, Holder holder)
    {
        var engine = new ReloadDecisionEngine(TimeProvider.System);
        var source = new FakeConfigReloadSource();
        var target = new RecordingPanelConfigApplyTarget();
        var notifier = new RecordingUserNotifier();
        var pipeline = new ConfigReloadPipeline(
            source,
            engine,
            new PanelConfigCandidateReader(temp.Config, logger: null),
            new PanelConfigApplier(target, logger: null),
            () => holder.Value,
            config => holder.Value = config,
            notifier,
            TimeProvider.System,
            health: null,
            failureNotificationInterval: TimeSpan.FromMinutes(1),
            logger: null);

        var harness = new Harness(pipeline, source, target, notifier);
        pipeline.Start();
        return harness;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(15);
        }

        Assert.True(condition(), "The reload pipeline did not reach the expected state within the timeout.");
    }

    private sealed class Holder(PanelConfig value)
    {
        public PanelConfig Value { get; set; } = value;
    }

    private sealed class Harness : IDisposable
    {
        public Harness(ConfigReloadPipeline pipeline, FakeConfigReloadSource source, RecordingPanelConfigApplyTarget target, RecordingUserNotifier notifier)
        {
            Pipeline = pipeline;
            Source = source;
            Target = target;
            Notifier = notifier;
        }

        public ConfigReloadPipeline Pipeline { get; }

        public FakeConfigReloadSource Source { get; }

        public RecordingPanelConfigApplyTarget Target { get; }

        public RecordingUserNotifier Notifier { get; }

        public void Dispose() => Pipeline.Dispose();
    }
}
