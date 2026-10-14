using Serilog.Events;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Services.Logging;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Button interaction semantics (T-10, RF-030/RF-035/RF-047/RF-050): exactly one action execution
/// per completed tap, no execution while disabled or unbound, failures surfaced as a transient
/// error state plus a redaction-safe log entry, and cancellation treated as non-failure.
/// </summary>
public sealed class ButtonWidgetTests
{
    [Fact]
    public async Task ActivateAsync_OneCompletedTap_InvokesTheBoundActionExactlyOnce()
    {
        var executor = new RecordingActionExecutor();
        var action = new MuteActionConfig();
        var viewModel = new ButtonWidgetViewModel(CreateConfig(action), executor);

        await viewModel.ActivateAsync();

        var invocation = Assert.Single(executor.Invocations);
        Assert.Same(action, invocation.Action);
        Assert.Equal("button-1", invocation.WidgetId);
        Assert.False(viewModel.HasError);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ActivateAsync_TwoCompletedTaps_InvokeOnceEach()
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new ButtonWidgetViewModel(CreateConfig(new MuteActionConfig()), executor);

        await viewModel.ActivateAsync();
        await viewModel.ActivateAsync();

        Assert.Equal(2, executor.InvocationCount);
    }

    [Fact]
    public void ActivateCommand_Execute_InvokesTheBoundActionOnce()
    {
        // The bindable command and the text tap path share one interaction implementation; the
        // synchronous double lets the whole asynchronous chain complete before Execute returns.
        var executor = new RecordingActionExecutor();
        var viewModel = new ButtonWidgetViewModel(CreateConfig(new MuteActionConfig()), executor);

        viewModel.ActivateCommand.Execute(null);

        Assert.Equal(1, executor.InvocationCount);
    }

    [Fact]
    public async Task ActivateAsync_FailedResult_ShowsTheTransientErrorAndLogsIt()
    {
        var (logger, sink) = TestLoggerFactory.Create();
        var executor = new RecordingActionExecutor
        {
            ResultFactory = (_, _) => ActionExecutionResult.Failed("tool missing"),
        };
        var viewModel = new ButtonWidgetViewModel(
            CreateConfig(new MuteActionConfig()),
            executor,
            new FakeTimeProvider(),
            logger: logger);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.HasError);
        Assert.Equal("tool missing", viewModel.ErrorMessage);

        var warning = Assert.Single(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
        var message = warning.RenderMessage();
        Assert.Contains("tool missing", message, StringComparison.Ordinal);
        Assert.Contains("button-1", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActivateAsync_FailedResultWithSecret_MasksItBeforeDisplayingAndLogging()
    {
        var (logger, sink) = TestLoggerFactory.Create();
        var executor = new RecordingActionExecutor
        {
            ResultFactory = (_, _) => ActionExecutionResult.Failed("password=hunter2"),
        };
        var viewModel = new ButtonWidgetViewModel(
            CreateConfig(new MuteActionConfig()),
            executor,
            new FakeTimeProvider(),
            logger: logger);

        await viewModel.ActivateAsync();

        Assert.DoesNotContain("hunter2", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(LogRedactor.Mask, viewModel.ErrorMessage, StringComparison.Ordinal);

        var logged = string.Join("|", sink.Events.Select(logEvent => logEvent.RenderMessage()));
        Assert.DoesNotContain("hunter2", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActivateAsync_InEditMode_DoesNotExecuteOrFlagAnError()
    {
        // M2-RF-439: a tap that reaches the widget command while editing must not run the action
        // nor show a transient error badge.
        var executor = new RecordingActionExecutor();
        var viewModel = new ButtonWidgetViewModel(CreateConfig(new MuteActionConfig()), executor);
        viewModel.SetEditMode(true);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsEditMode);
        Assert.Empty(executor.Invocations);
        Assert.False(viewModel.HasError);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public void ActivateCommand_InEditMode_DoesNotExecute()
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new ButtonWidgetViewModel(CreateConfig(new MuteActionConfig()), executor);
        viewModel.SetEditMode(true);

        viewModel.ActivateCommand.Execute(null);

        Assert.Equal(0, executor.InvocationCount);
    }

    [Fact]
    public async Task ActivateAsync_AfterEditModeEnds_ExecutesAgain()
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new ButtonWidgetViewModel(CreateConfig(new MuteActionConfig()), executor);
        viewModel.SetEditMode(true);

        await viewModel.ActivateAsync();
        Assert.Equal(0, executor.InvocationCount);

        viewModel.SetEditMode(false);
        await viewModel.ActivateAsync();

        Assert.False(viewModel.IsEditMode);
        Assert.Equal(1, executor.InvocationCount);
    }

    [Fact]
    public async Task ActivateAsync_DisabledWidget_ExecutesNothing()
    {
        var executor = new RecordingActionExecutor();
        var config = CreateConfig(new MuteActionConfig());
        config.IsEnabled = false;
        config.DisabledReason = "Widget binds an unsupported action type and was disabled.";
        var viewModel = new ButtonWidgetViewModel(config, executor);

        await viewModel.ActivateAsync();

        Assert.Empty(executor.Invocations);
        Assert.False(viewModel.IsEnabled);
        Assert.Equal(config.DisabledReason, viewModel.DisabledReason);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task ActivateAsync_NoBoundAction_ExecutesNothing()
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new ButtonWidgetViewModel(CreateConfig(action: null), executor);

        await viewModel.ActivateAsync();

        Assert.Empty(executor.Invocations);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task ActivateAsync_CanceledResult_IsNotAnError()
    {
        var executor = new RecordingActionExecutor
        {
            ResultFactory = (_, _) => ActionExecutionResult.CanceledResult(),
        };
        var viewModel = new ButtonWidgetViewModel(
            CreateConfig(new MuteActionConfig()),
            executor,
            new FakeTimeProvider());

        await viewModel.ActivateAsync();

        Assert.False(viewModel.HasError);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ActivateAsync_ExecutorThrows_IsContainedAndSurfacedAsAnError()
    {
        var (logger, sink) = TestLoggerFactory.Create();
        var executor = new RecordingActionExecutor
        {
            ThrowOnExecute = new InvalidOperationException("boom"),
        };
        var viewModel = new ButtonWidgetViewModel(
            CreateConfig(new MuteActionConfig()),
            executor,
            new FakeTimeProvider(),
            logger: logger);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.HasError);
        Assert.Contains(
            sink.Events,
            logEvent => logEvent.Level == LogEventLevel.Warning && logEvent.Exception is InvalidOperationException);
    }

    private static ButtonWidgetConfig CreateConfig(ActionConfig? action) => new()
    {
        Id = "button-1",
        X = 16,
        Y = 24,
        Width = 120,
        Height = 64,
        Label = "Mute",
        Action = action,
    };
}
