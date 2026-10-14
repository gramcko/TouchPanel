using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Monitoring;
using TouchPanel.Tests.Monitoring;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Widgets;

/// <summary>
/// Registry/template mapping (T-10/T-11, RF-032/RF-033/DD-6): the default catalog registers Button,
/// Toggle, Label, and Gauge with distinct templates, each type projects to its view model with the
/// shared dependencies, unknown types resolve to the disabled unsupported placeholder, and a new
/// widget type is one registration without any host change.
/// </summary>
public sealed class WidgetRegistryTests
{
    [Fact]
    public void CreateDefault_RegistersTheDefaultWidgetCatalogOnce()
    {
        var registry = WidgetRegistry.CreateDefault();

        Assert.Equal(10, registry.Registrations.Count);
        Assert.Equal(WidgetTemplateKeys.Button, registry.GetTemplateKey(WidgetTypes.Button));
        Assert.Equal(WidgetTemplateKeys.Toggle, registry.GetTemplateKey(WidgetTypes.Toggle));
        Assert.Equal(WidgetTemplateKeys.Label, registry.GetTemplateKey(WidgetTypes.Label));
        Assert.Equal(WidgetTemplateKeys.Gauge, registry.GetTemplateKey(WidgetTypes.Gauge));
        Assert.Equal(WidgetTemplateKeys.Slider, registry.GetTemplateKey(WidgetTypes.Slider));
        Assert.Equal(WidgetTemplateKeys.Sparkline, registry.GetTemplateKey(WidgetTypes.Sparkline));
        Assert.Equal(WidgetTemplateKeys.Clock, registry.GetTemplateKey(WidgetTypes.Clock));
        Assert.Equal(WidgetTemplateKeys.ImageButton, registry.GetTemplateKey(WidgetTypes.ImageButton));
        Assert.Equal(WidgetTemplateKeys.Bar, registry.GetTemplateKey(WidgetTypes.Bar));
        Assert.Equal(WidgetTemplateKeys.Image, registry.GetTemplateKey(WidgetTypes.Image));
    }

    [Fact]
    public void Create_ProjectsEachRegisteredTypeToItsViewModel()
    {
        var registry = WidgetRegistry.CreateDefault();

        Assert.IsType<ButtonWidgetViewModel>(registry.Create(new ButtonWidgetConfig { Id = "b" }));
        Assert.IsType<ToggleWidgetViewModel>(registry.Create(new ToggleWidgetConfig { Id = "t" }));
        Assert.IsType<LabelWidgetViewModel>(registry.Create(new LabelWidgetConfig { Id = "l" }));
        Assert.IsType<GaugeWidgetViewModel>(registry.Create(new GaugeWidgetConfig { Id = "g", Sensor = "cpu.total" }));
        Assert.IsType<SliderWidgetViewModel>(registry.Create(new SliderWidgetConfig { Id = "s" }));
        Assert.IsType<SparklineWidgetViewModel>(registry.Create(new SparklineWidgetConfig { Id = "sp", Sensor = "cpu.total" }));
        Assert.IsType<ClockWidgetViewModel>(registry.Create(new ClockWidgetConfig { Id = "c" }));
        Assert.IsType<ImageButtonWidgetViewModel>(registry.Create(new ImageButtonWidgetConfig { Id = "ib" }));
        Assert.IsType<BarWidgetViewModel>(registry.Create(new BarWidgetConfig { Id = "bar", Sensor = "cpu.total" }));
        Assert.IsType<ImageWidgetViewModel>(registry.Create(new ImageWidgetConfig { Id = "img" }));
    }

    [Fact]
    public void TryGet_UnregisteredType_ReturnsFalse()
    {
        var registry = WidgetRegistry.CreateDefault();

        Assert.False(registry.TryGet("heatmap", out _));
        Assert.Equal(WidgetTemplateKeys.Unsupported, registry.GetTemplateKey("heatmap"));
    }

    [Fact]
    public void Create_SparklineWidget_ConsumesTheSharedHistoryAndRequestsTracking()
    {
        var history = new FakeSensorHistory();
        var requested = new List<string>();
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext
        {
            History = history,
            RequestHistoryTracking = requested.Add,
        });

        history.Set(SensorKeys.CpuTotal, 42);

        var sparkline = Assert.IsType<SparklineWidgetViewModel>(
            registry.Create(new SparklineWidgetConfig { Id = "sp", Sensor = SensorKeys.CpuTotal }));

        Assert.Equal([SensorKeys.CpuTotal], requested);
        Assert.Equal(new double?[] { 42 }, sparkline.Points);
    }

    [Fact]
    public void Create_UnregisteredType_ReturnsADisabledUnsupportedPlaceholder()
    {
        var registry = WidgetRegistry.CreateDefault();

        var widget = Assert.IsType<UnsupportedWidgetViewModel>(registry.Create(new StubWidgetConfig()));

        Assert.False(widget.IsEnabled);
        Assert.Equal(WidgetTemplateKeys.Unsupported, widget.TemplateKey);
        Assert.Contains("custom", widget.UnsupportedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_UnsupportedWidget_WithRawNonFiniteGeometry_SanitizesTheBounds()
    {
        var registry = WidgetRegistry.CreateDefault();
        var config = new StubWidgetConfig
        {
            Id = "g",
            X = double.NaN,
            Y = double.NaN,
            Width = double.NaN,
            Height = double.NaN,
        };

        var widget = registry.Create(config);

        Assert.Equal(0, widget.X);
        Assert.Equal(0, widget.Y);
        Assert.Equal(ConfigValidator.MinWidgetSize, widget.Width);
        Assert.Equal(ConfigValidator.MinWidgetSize, widget.Height);
    }

    [Fact]
    public async Task Create_PassesTheSharedActionExecutorToInteractiveWidgets()
    {
        var executor = new RecordingActionExecutor();
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext { ActionExecutor = executor });

        var button = Assert.IsType<ButtonWidgetViewModel>(
            registry.Create(new ButtonWidgetConfig { Id = "b", Action = new MuteActionConfig() }));

        await button.ActivateAsync();

        var invocation = Assert.Single(executor.Invocations);
        Assert.Equal("b", invocation.WidgetId);
    }

    [Fact]
    public void Create_GaugeWidget_ConsumesTheSharedSnapshotSource()
    {
        var source = new FakeSnapshotSource();
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext { SnapshotSource = source });

        var gauge = Assert.IsType<GaugeWidgetViewModel>(
            registry.Create(new GaugeWidgetConfig { Id = "g", Sensor = SensorKeys.CpuTotal }));

        source.Publish(new SensorSnapshot { CpuTotalPercent = 50 });

        Assert.Equal("50 %", gauge.ValueText);
    }

    [Fact]
    public void Create_PassesTheSharedTimeProviderAndDispatcherToInteractiveWidgets()
    {
        var clock = new FakeTimeProvider();
        var dispatched = new List<Action>();
        var executor = new RecordingActionExecutor
        {
            ResultFactory = (_, _) => ActionExecutionResult.Failed("boom"),
        };
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext
        {
            ActionExecutor = executor,
            TimeProvider = clock,
            Dispatch = action =>
            {
                dispatched.Add(action);
                action();
            },
        });

        var button = (ButtonWidgetViewModel)registry.Create(
            new ButtonWidgetConfig { Id = "b", Action = new MuteActionConfig() });

        Assert.False(button.HasError);

        // Fire the interaction, then advance the injected clock to prove the error window is
        // driven by the shared TimeProvider through the shared dispatcher.
        button.ActivateCommand.Execute(null);
        Assert.True(button.HasError);
        Assert.NotEmpty(dispatched);

        clock.Advance(WidgetViewModelBase.ErrorDisplayDuration);
        Assert.False(button.HasError);
    }

    [Fact]
    public void BuiltInWidgets_ListsTheBuiltInTypesInRegistrationOrder()
    {
        var registry = WidgetRegistry.CreateDefault();

        var items = registry.BuiltInWidgets;
        Assert.Equal(10, items.Count);
        Assert.Equal(
            new[]
            {
                WidgetTypes.Button,
                WidgetTypes.Toggle,
                WidgetTypes.Label,
                WidgetTypes.Gauge,
                WidgetTypes.Slider,
                WidgetTypes.Sparkline,
                WidgetTypes.Clock,
                WidgetTypes.ImageButton,
                WidgetTypes.Bar,
                WidgetTypes.Image,
            },
            items.Select(item => item.Type));
        Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.DisplayName)));
    }

    [Fact]
    public void TryCreateDefaultWidget_ProducesValidTypeDefaults()
    {
        var registry = WidgetRegistry.CreateDefault();

        Assert.True(registry.TryCreateDefaultWidget(WidgetTypes.Button, out var button));
        Assert.IsType<MuteActionConfig>(Assert.IsType<ButtonWidgetConfig>(button).Action);

        Assert.True(registry.TryCreateDefaultWidget(WidgetTypes.Gauge, out var gauge));
        Assert.Equal(SensorKeys.CpuTotal, Assert.IsType<GaugeWidgetConfig>(gauge).Sensor);

        Assert.True(registry.TryCreateDefaultWidget(WidgetTypes.Slider, out var slider));
        var sliderConfig = Assert.IsType<SliderWidgetConfig>(slider);
        Assert.Equal(50, sliderConfig.Value);
        Assert.Equal(VolumeMode.Set, Assert.IsType<VolumeActionConfig>(sliderConfig.Action).Mode);

        Assert.True(registry.TryCreateDefaultWidget(WidgetTypes.Sparkline, out var sparkline));
        Assert.Equal(SensorKeys.CpuTotal, Assert.IsType<SparklineWidgetConfig>(sparkline).Sensor);

        Assert.True(registry.TryCreateDefaultWidget(WidgetTypes.Clock, out var clock));
        Assert.Equal(ClockMode.Time24, Assert.IsType<ClockWidgetConfig>(clock).Mode);

        Assert.True(registry.TryCreateDefaultWidget(WidgetTypes.ImageButton, out var imageButton));
        var imageButtonConfig = Assert.IsType<ImageButtonWidgetConfig>(imageButton);
        Assert.True(imageButtonConfig.ShowLabel);
        Assert.Equal("power", imageButtonConfig.Icon);
        Assert.IsType<MuteActionConfig>(imageButtonConfig.Action);

        Assert.True(registry.TryCreateDefaultWidget(WidgetTypes.Bar, out var bar));
        var barConfig = Assert.IsType<BarWidgetConfig>(bar);
        Assert.Equal(SensorKeys.CpuTotal, barConfig.Sensor);
        Assert.Equal(BarWidgetConfig.DefaultMinimum, barConfig.Minimum);
        Assert.Equal(BarWidgetConfig.DefaultMaximum, barConfig.Maximum);
        Assert.True(barConfig.ShowValue);

        Assert.True(registry.TryCreateDefaultWidget(WidgetTypes.Image, out var image));
        var imageConfig = Assert.IsType<ImageWidgetConfig>(image);
        Assert.Equal(ImageStretchMode.Uniform, imageConfig.Stretch);
        Assert.Null(imageConfig.Path);
    }

    [Fact]
    public void BuiltInWidgets_NewTypesCarryTheRegisteredDefaultGeometry()
    {
        var registry = WidgetRegistry.CreateDefault();

        var clock = registry.BuiltInWidgets.Single(item => item.Type == WidgetTypes.Clock);
        Assert.Equal(240, clock.DefaultWidth);
        Assert.Equal(120, clock.DefaultHeight);

        var imageButton = registry.BuiltInWidgets.Single(item => item.Type == WidgetTypes.ImageButton);
        Assert.Equal(120, imageButton.DefaultWidth);
        Assert.Equal(120, imageButton.DefaultHeight);

        var bar = registry.BuiltInWidgets.Single(item => item.Type == WidgetTypes.Bar);
        Assert.Equal(240, bar.DefaultWidth);
        Assert.Equal(64, bar.DefaultHeight);

        var image = registry.BuiltInWidgets.Single(item => item.Type == WidgetTypes.Image);
        Assert.Equal(240, image.DefaultWidth);
        Assert.Equal(160, image.DefaultHeight);
    }

    [Fact]
    public void Create_ClockWidget_ConsumesTheSharedClockStateSource()
    {
        var stateSource = new FakeClockStateSource();
        var clock = new FakeTimeProvider { UtcNow = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero) };
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext
        {
            ClockStateSource = stateSource,
            TimeProvider = clock,
        });

        using var widget = (ClockWidgetViewModel)registry.Create(new ClockWidgetConfig
        {
            Id = "c",
            Mode = ClockMode.Time24,
        });

        Assert.Equal("12:00", widget.DisplayText);

        // Advance past the boundary, then raise the OS-state event: the clock recomputes without a
        // new timer tick being due yet.
        clock.UtcNow = new DateTimeOffset(2026, 1, 1, 12, 5, 0, TimeSpan.Zero);
        stateSource.Raise();

        Assert.Equal("12:05", widget.DisplayText);
    }

    [Fact]
    public void TryCreateDefaultWidget_UnknownType_ReturnsFalse()
    {
        var registry = WidgetRegistry.CreateDefault();

        Assert.False(registry.TryCreateDefaultWidget("heatmap", out var config));
        Assert.Null(config);
    }

    [Fact]
    public void Register_DuplicateType_Throws()
    {
        var registry = WidgetRegistry.CreateDefault();

        Assert.Throws<ArgumentException>(() => registry.Register(new WidgetRegistration(
            WidgetTypes.Button,
            WidgetTemplateKeys.Button,
            (config, _) => new ButtonWidgetViewModel((ButtonWidgetConfig)config))));
    }

    [Fact]
    public void Register_NewType_IsAvailableWithoutChangingTheHost()
    {
        var registry = WidgetRegistry.CreateDefault();
        registry.Register(new WidgetRegistration(
            "custom",
            "Widget.Custom",
            (config, _) => new StubWidgetViewModel(config)));

        var widget = Assert.IsType<StubWidgetViewModel>(registry.Create(new StubWidgetConfig()));

        Assert.Equal("Widget.Custom", widget.TemplateKey);
        Assert.Equal("Widget.Custom", registry.GetTemplateKey("custom"));
    }

    private sealed class StubWidgetConfig : WidgetConfig
    {
        public override string Type => "custom";
    }

    private sealed class StubWidgetViewModel : WidgetViewModelBase
    {
        public StubWidgetViewModel(WidgetConfig config)
            : base(config, "Widget.Custom")
        {
        }
    }
}
