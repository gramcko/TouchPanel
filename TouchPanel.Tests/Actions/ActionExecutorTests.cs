using System.Text.Json;
using Serilog;
using Serilog.Events;
using TouchPanel.Actions;
using TouchPanel.Actions.Handlers;
using TouchPanel.Config;
using TouchPanel.Services.Logging;
using TouchPanel.Services.Processes;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Actions;

/// <summary>
/// RF-047/RF-050/RF-051 executor behavior: dispatch by config type, unknown types fail fast, every
/// handler failure (thrown or returned) is contained and logged with the widget id, cancellation is
/// not a failure, and parameter-bearing text is redacted (RF-085).
/// </summary>
public class ActionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_KnownType_DispatchesToMatchingHandlerOnly()
    {
        var matching = new StubActionHandler(ActionTypes.Mute);
        var other = new StubActionHandler(ActionTypes.MediaKey);
        var (executor, _) = Create(matching, other);
        var action = new MuteActionConfig();

        var result = await executor.ExecuteAsync(action, "w1");

        Assert.True(result.Success);
        Assert.False(result.Canceled);
        Assert.Equal(1, matching.InvocationCount);
        Assert.Same(action, matching.LastAction);
        Assert.Equal(0, other.InvocationCount);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownType_FailsFastWithoutDispatching()
    {
        var handler = new StubActionHandler(ActionTypes.Mute);
        var (executor, sink) = Create(handler);

        var result = await executor.ExecuteAsync(UnknownAction("http"), "w7");

        Assert.False(result.Success);
        Assert.False(result.Canceled);
        Assert.Contains("http", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.InvocationCount);

        var warning = Assert.Single(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
        var message = warning.RenderMessage();
        Assert.Contains("http", message, StringComparison.Ordinal);
        Assert.Contains("w7", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerThrows_ReturnsFailureAndLogsTheException()
    {
        var failure = new InvalidOperationException("boom");
        var handler = new StubActionHandler(ActionTypes.Mute, (_, _) => throw failure);
        var (executor, sink) = Create(handler);

        var result = await executor.ExecuteAsync(new MuteActionConfig(), "w2");

        Assert.False(result.Success);
        Assert.Contains("boom", result.Message, StringComparison.Ordinal);

        var warning = Assert.Single(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
        Assert.Same(failure, warning.Exception);
        Assert.Contains("w2", warning.RenderMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerReturnsFailure_MapsMessageAndLogs()
    {
        var handler = new StubActionHandler(
            ActionTypes.Mute,
            (_, _) => Task.FromResult(ActionExecutionResult.Failed("process missing")));
        var (executor, sink) = Create(handler);

        var result = await executor.ExecuteAsync(new MuteActionConfig(), "w3");

        Assert.False(result.Success);
        Assert.Equal("process missing", result.Message);
        Assert.Contains(
            sink.Events,
            logEvent => logEvent.Level == LogEventLevel.Warning
                && logEvent.RenderMessage().Contains("process missing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_PreCanceledToken_ReturnsCanceledWithoutDispatching()
    {
        var handler = new StubActionHandler(ActionTypes.Mute);
        var (executor, _) = Create(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await executor.ExecuteAsync(new MuteActionConfig(), "w4", cancellation.Token);

        Assert.True(result.Canceled);
        Assert.False(result.Success);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerCancels_ReturnsCanceledResultWithoutWarning()
    {
        var handler = new StubActionHandler(ActionTypes.Mute, (_, _) => throw new OperationCanceledException());
        var (executor, sink) = Create(handler);

        var result = await executor.ExecuteAsync(new MuteActionConfig(), "w5");

        Assert.True(result.Canceled);
        Assert.False(result.Success);
        Assert.DoesNotContain(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
    }

    [Fact]
    public async Task ExecuteAsync_LogsRunCommandParametersRedacted()
    {
        var handler = new StubActionHandler(ActionTypes.RunCommand);
        var (executor, sink) = Create(handler);
        var action = new RunCommandActionConfig
        {
            Path = "tool.exe",
            Arguments = ["--token=supersecret", "plain"],
        };

        var result = await executor.ExecuteAsync(action, "w6");

        Assert.True(result.Success);
        var start = Assert.Single(sink.Events, logEvent => logEvent.Level == LogEventLevel.Debug);
        var message = start.RenderMessage();
        Assert.DoesNotContain("supersecret", message, StringComparison.Ordinal);
        Assert.Contains("token=***", message, StringComparison.Ordinal);
        Assert.Contains("plain", message, StringComparison.Ordinal);
        Assert.Contains("w6", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_FailureMessageWithSecret_IsRedactedBeforeReturningAndLogging()
    {
        var handler = new StubActionHandler(
            ActionTypes.Mute,
            (_, _) => Task.FromResult(ActionExecutionResult.Failed("password=hunter2")));
        var (executor, sink) = Create(handler);

        var result = await executor.ExecuteAsync(new MuteActionConfig(), "w8");

        Assert.DoesNotContain("hunter2", result.Message, StringComparison.Ordinal);
        Assert.Contains(LogRedactor.Mask, result.Message, StringComparison.Ordinal);

        var warning = Assert.Single(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
        Assert.DoesNotContain("hunter2", warning.RenderMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RunCommandHandlerWithFailingLauncher_ContainsFailure()
    {
        var launcher = new RecordingProcessLauncher { Result = ProcessLaunchResult.Failed("access denied") };
        var (executor, sink) = Create(new RunCommandHandler(launcher));

        var result = await executor.ExecuteAsync(
            new RunCommandActionConfig { Path = "tool.exe" },
            "w9");

        Assert.False(result.Success);
        Assert.Equal("access denied", result.Message);
        Assert.Contains(
            sink.Events,
            logEvent => logEvent.Level == LogEventLevel.Warning
                && logEvent.RenderMessage().Contains("access denied", StringComparison.Ordinal));
    }

    [Fact]
    public void Constructor_DuplicateHandlerType_Throws()
    {
        var handlers = new IActionHandler[]
        {
            new StubActionHandler(ActionTypes.Mute),
            new StubActionHandler(ActionTypes.Mute),
        };

        Assert.Throws<ArgumentException>(() => new ActionExecutor(handlers));
    }

    [Fact]
    public async Task ExecuteAsync_SendKeys_DispatchesThroughExecutorAndSynthesizesCombination()
    {
        var input = new RecordingKeyInput();
        var (executor, _) = Create(new SendKeysHandler(input));
        var action = new SendKeysActionConfig { Key = "A", Modifiers = ["Alt"] };

        var result = await executor.ExecuteAsync(action, "w10");

        Assert.True(result.Success);
        Assert.Equal(new ushort[] { 0x12, 0x41 }, input.DownKeys);
        Assert.Equal(new ushort[] { 0x41, 0x12 }, input.UpKeys);
    }

    [Fact]
    public async Task ExecuteAsync_NullAction_ThrowsArgumentNull()
    {
        var (executor, _) = Create();

        await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ExecuteAsync(null!));
    }

    private static UnknownActionConfig UnknownAction(string type)
    {
        using var document = JsonDocument.Parse("{}");
        return new UnknownActionConfig(type, document.RootElement.Clone());
    }

    private static (ActionExecutor Executor, RecordingSink Sink) Create(params IActionHandler[] handlers)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new ActionExecutor(handlers, logger), sink);
    }
}
