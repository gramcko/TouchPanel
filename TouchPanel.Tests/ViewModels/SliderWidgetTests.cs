using System.Windows.Controls;
using Serilog.Events;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Slider value model (T-06, RF-040/M2-RF-401..406): range/step/orientation/value normalization
/// including hostile input, value→<c>volume Set</c> mapping onto <c>0–100</c>, throttled emission
/// during a drag, a single non-duplicated emission on completion, unsupported-action disablement,
/// and transient failure feedback. Timing is driven by the injected <see cref="FakeTimeProvider"/>
/// so the throttle assertions are deterministic; the action boundary is the shared
/// <see cref="RecordingActionExecutor"/>.
/// </summary>
public sealed class SliderWidgetTests
{
    [Fact]
    public void Constructor_MapsRangeStepOrientationAndSnappedValue()
    {
        var viewModel = new SliderWidgetViewModel(new SliderWidgetConfig
        {
            Id = "slider-1",
            Width = 40,
            Height = 40,
            Minimum = 0,
            Maximum = 100,
            Step = 5,
            Orientation = SliderOrientation.Vertical,
            Value = 42,
        });

        Assert.Equal(0, viewModel.Minimum);
        Assert.Equal(100, viewModel.Maximum);
        Assert.Equal(5, viewModel.Step);
        Assert.Equal(Orientation.Vertical, viewModel.Orientation);
        Assert.Equal(40, viewModel.Value);
        Assert.Equal("40", viewModel.ValueText);
    }

    [Fact]
    public void Constructor_UnknownOrientation_FallsBackToHorizontal()
    {
        var viewModel = new SliderWidgetViewModel(CreateConfig(orientation: SliderOrientation.Unknown));

        Assert.Equal(Orientation.Horizontal, viewModel.Orientation);
    }

    [Theory]
    [InlineData(double.NaN, double.NaN, -1, double.NaN)]
    [InlineData(10, 10, 1, 0)]
    [InlineData(5, 1, 1, 0)]
    public void Constructor_HostileRangeStepAndValue_AreNormalized(double minimum, double maximum, double step, double value)
    {
        var viewModel = new SliderWidgetViewModel(new SliderWidgetConfig
        {
            Id = "slider-1",
            Width = 40,
            Height = 40,
            Minimum = minimum,
            Maximum = maximum,
            Step = step,
            Value = value,
        });

        Assert.Equal(0, viewModel.Minimum);
        Assert.Equal(100, viewModel.Maximum);
        Assert.True(viewModel.Step > 0);
        Assert.True(viewModel.Step <= viewModel.Maximum - viewModel.Minimum);
        Assert.True(viewModel.Value >= viewModel.Minimum && viewModel.Value <= viewModel.Maximum);
    }

    [Fact]
    public void Value_SnapsToStepAndClampsIntoRange()
    {
        var viewModel = new SliderWidgetViewModel(CreateConfig(minimum: 0, maximum: 10, step: 2));

        viewModel.Value = 5;
        Assert.Equal(6, viewModel.Value);

        viewModel.Value = 40;
        Assert.Equal(10, viewModel.Value);

        viewModel.Value = -5;
        Assert.Equal(0, viewModel.Value);
    }

    [Fact]
    public async Task Value_VolumeSetBinding_MapsOnto0To100()
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new SliderWidgetViewModel(
            CreateConfig(minimum: 0, maximum: 10, step: 1, action: new VolumeActionConfig { Mode = VolumeMode.Set }),
            executor);

        viewModel.Value = 5;
        await viewModel.CompleteInteractionAsync();

        var invocation = Assert.Single(executor.Invocations);
        var volume = Assert.IsType<VolumeActionConfig>(invocation.Action);
        Assert.Equal(VolumeMode.Set, volume.Mode);
        Assert.Equal(50, volume.Level);
        Assert.Equal("slider-1", invocation.WidgetId);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 100)]
    [InlineData(3, 30)]
    public async Task Value_EndpointsAndMidpoints_MapToExpectedLevel(double sliderValue, int expectedLevel)
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new SliderWidgetViewModel(
            CreateConfig(minimum: 0, maximum: 10, step: 1, action: new VolumeActionConfig { Mode = VolumeMode.Set }),
            executor);

        viewModel.Value = sliderValue;
        await viewModel.CompleteInteractionAsync();

        var volume = Assert.IsType<VolumeActionConfig>(Assert.Single(executor.Invocations).Action);
        Assert.Equal(expectedLevel, volume.Level);
    }

    [Fact]
    public async Task Value_ThrottledDuringDrag_EmitsAtMostOncePerWindow()
    {
        var clock = new FakeTimeProvider();
        var executor = new RecordingActionExecutor();
        var viewModel = new SliderWidgetViewModel(
            CreateConfig(minimum: 0, maximum: 100, step: 1, action: new VolumeActionConfig { Mode = VolumeMode.Set }),
            executor,
            clock);

        viewModel.Value = 10;
        Assert.Equal(1, executor.InvocationCount);

        // Same clock tick: the second change is suppressed by the 100 ms throttle.
        viewModel.Value = 20;
        viewModel.Value = 30;
        Assert.Equal(1, executor.InvocationCount);

        clock.Advance(SliderWidgetViewModel.EmissionThrottle);
        viewModel.Value = 40;
        Assert.Equal(2, executor.InvocationCount);

        await viewModel.CompleteInteractionAsync();
    }

    [Fact]
    public async Task CompleteInteraction_EmitsTheFinalValueOnceAndNeverDuplicates()
    {
        var clock = new FakeTimeProvider();
        var executor = new RecordingActionExecutor();
        var viewModel = new SliderWidgetViewModel(
            CreateConfig(minimum: 0, maximum: 100, step: 1, action: new VolumeActionConfig { Mode = VolumeMode.Set }),
            executor,
            clock);

        viewModel.Value = 10;
        clock.Advance(TimeSpan.FromMilliseconds(50));
        viewModel.Value = 25; // throttled: the 100 ms window has not elapsed

        Assert.Equal(1, executor.InvocationCount);

        await viewModel.CompleteInteractionAsync();
        Assert.Equal(2, executor.InvocationCount);
        var last = Assert.IsType<VolumeActionConfig>(executor.Invocations[^1].Action);
        Assert.Equal(25, last.Level);

        // Releasing again at the same value must not duplicate the emission.
        await viewModel.CompleteInteractionAsync();
        Assert.Equal(2, executor.InvocationCount);
    }

    [Fact]
    public async Task CompleteInteraction_WhenFinalValueAlreadyEmitted_DoesNotDuplicate()
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new SliderWidgetViewModel(
            CreateConfig(minimum: 0, maximum: 100, step: 1, action: new VolumeActionConfig { Mode = VolumeMode.Set }),
            executor);

        viewModel.Value = 10; // emitted immediately (first emission of the interaction)
        Assert.Equal(1, executor.InvocationCount);

        await viewModel.CompleteInteractionAsync();
        Assert.Equal(1, executor.InvocationCount);
    }

    [Fact]
    public async Task UnsupportedAction_DisablesTheSliderAndLogs()
    {
        var (logger, sink) = TestLoggerFactory.Create();
        var executor = new RecordingActionExecutor();
        var viewModel = new SliderWidgetViewModel(
            CreateConfig(action: new MuteActionConfig()),
            executor,
            logger: logger);

        Assert.False(viewModel.IsEnabled);
        Assert.True(viewModel.ShowsDisabledIndicator);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.DisabledReason));
        Assert.Contains("volume", viewModel.DisabledReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);

        viewModel.Value = 50;
        await viewModel.CompleteInteractionAsync();

        Assert.Empty(executor.Invocations);
    }

    [Fact]
    public async Task NoBoundAction_StaysEnabledAsDisplayOnly()
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new SliderWidgetViewModel(CreateConfig(action: null), executor);

        Assert.True(viewModel.IsEnabled);
        Assert.False(viewModel.ShowsDisabledIndicator);

        viewModel.Value = 50;
        await viewModel.CompleteInteractionAsync();

        Assert.Empty(executor.Invocations);
    }

    [Fact]
    public async Task DisabledSlider_DoesNotEmit()
    {
        var executor = new RecordingActionExecutor();
        var config = CreateConfig(action: new VolumeActionConfig { Mode = VolumeMode.Set });
        config.IsEnabled = false;
        config.DisabledReason = "kept for diagnosis";
        var viewModel = new SliderWidgetViewModel(config, executor);

        viewModel.Value = 50;
        await viewModel.CompleteInteractionAsync();

        Assert.Empty(executor.Invocations);
    }

    [Fact]
    public async Task FailedEmission_ShowsTheTransientError()
    {
        var executor = new RecordingActionExecutor
        {
            ResultFactory = (_, _) => ActionExecutionResult.Failed("no audio device"),
        };
        var viewModel = new SliderWidgetViewModel(
            CreateConfig(action: new VolumeActionConfig { Mode = VolumeMode.Set }),
            executor,
            new FakeTimeProvider());

        viewModel.Value = 50;

        Assert.True(viewModel.HasError);
        Assert.Equal("no audio device", viewModel.ErrorMessage);

        await viewModel.CompleteInteractionAsync();
    }

    private static SliderWidgetConfig CreateConfig(
        double minimum = 0,
        double maximum = 100,
        double step = 1,
        SliderOrientation orientation = SliderOrientation.Horizontal,
        ActionConfig? action = null) => new()
        {
            Id = "slider-1",
            X = 8,
            Y = 12,
            Width = 200,
            Height = 80,
            Label = "Volume",
            Minimum = minimum,
            Maximum = maximum,
            Step = step,
            Orientation = orientation,
            Value = minimum,
            Action = action,
        };
}
