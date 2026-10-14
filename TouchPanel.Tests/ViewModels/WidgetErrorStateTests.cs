using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Transient widget error-state timing (T-10, RF-036): the indicator appears on failure, clears
/// after approximately two seconds, restarts when another failure arrives, raises the expected
/// property notifications, and is released on disposal. Timing is driven by the injected
/// <see cref="FakeTimeProvider"/> so the assertions are deterministic.
/// </summary>
public sealed class WidgetErrorStateTests
{
    [Fact]
    public async Task FailedAction_ShowsTheErrorThenClearsItAfterAboutTwoSeconds()
    {
        var clock = new FakeTimeProvider();
        var viewModel = CreateFailingButton(clock);

        await viewModel.ActivateAsync();
        Assert.True(viewModel.HasError);
        Assert.Equal("boom", viewModel.ErrorMessage);

        clock.Advance(TimeSpan.FromSeconds(1.9));
        Assert.True(viewModel.HasError);

        clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.False(viewModel.HasError);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AnotherFailure_RestartsTheTwoSecondWindow()
    {
        var clock = new FakeTimeProvider();
        var viewModel = CreateFailingButton(clock);

        await viewModel.ActivateAsync();
        clock.Advance(TimeSpan.FromSeconds(1.5));

        await viewModel.ActivateAsync();
        clock.Advance(TimeSpan.FromSeconds(1.5));

        // The first window would have expired at t=2.0; the second failure restarted it at t=1.5.
        Assert.True(viewModel.HasError);

        clock.Advance(TimeSpan.FromMilliseconds(600));
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task ErrorTransitions_RaisePropertyChangedForStateAndMessage()
    {
        var clock = new FakeTimeProvider();
        var viewModel = CreateFailingButton(clock);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await viewModel.ActivateAsync();
        Assert.Contains(nameof(WidgetViewModelBase.HasError), changed);
        Assert.Contains(nameof(WidgetViewModelBase.ErrorMessage), changed);

        changed.Clear();
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Contains(nameof(WidgetViewModelBase.HasError), changed);
        Assert.Contains(nameof(WidgetViewModelBase.ErrorMessage), changed);
    }

    [Fact]
    public async Task Disposal_ReleasesThePendingErrorTimer()
    {
        var clock = new FakeTimeProvider();
        var viewModel = CreateFailingButton(clock);
        await viewModel.ActivateAsync();

        viewModel.Dispose();
        clock.Advance(TimeSpan.FromSeconds(5));

        // The timer was released, so no deferred callback may mutate the disposed view model.
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public async Task ErrorState_IsAppliedThroughTheUiDispatchDelegate()
    {
        var clock = new FakeTimeProvider();
        var dispatched = new List<Action>();
        var viewModel = new ButtonWidgetViewModel(
            CreateButtonConfig(),
            new RecordingActionExecutor { ResultFactory = (_, _) => ActionExecutionResult.Failed("x") },
            clock,
            dispatch: action =>
            {
                dispatched.Add(action);
                action();
            });

        await viewModel.ActivateAsync();

        Assert.NotEmpty(dispatched);
        Assert.True(viewModel.HasError);
    }

    private static ButtonWidgetViewModel CreateFailingButton(TimeProvider clock) => new(
        CreateButtonConfig(),
        new RecordingActionExecutor { ResultFactory = (_, _) => ActionExecutionResult.Failed("boom") },
        clock);

    private static ButtonWidgetConfig CreateButtonConfig() => new()
    {
        Id = "button-1",
        X = 0,
        Y = 0,
        Width = 80,
        Height = 40,
        Label = "Go",
        Action = new MuteActionConfig(),
    };
}
