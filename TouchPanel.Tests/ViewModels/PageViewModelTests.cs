using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Monitoring;
using TouchPanel.Tests.Monitoring;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Page construction (T-10, RF-032/RF-037/RF-047/RF-079): the active page is resolved with the
/// first-page fallback, widget geometry is preserved in DIPs in configuration order, unknown widget
/// types become disabled placeholders (never dropped), invalid action bindings keep their type's
/// template disabled, and the validated 40 DIP touch minimum survives into the view models.
/// </summary>
public sealed class PageViewModelTests
{
    [Fact]
    public void CreateForActivePage_UsesThePageMatchingActivePageId()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "second",
            Pages =
            [
                Page("first", new ButtonWidgetConfig { Id = "b", Width = 80, Height = 40, Action = new MuteActionConfig() }),
                Page("second", new LabelWidgetConfig { Id = "l", Width = 200, Height = 40, Label = "Title" }),
            ],
        };

        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());

        Assert.Equal("second", page.PageId);
        Assert.IsType<LabelWidgetViewModel>(Assert.Single(page.Widgets));
    }

    [Fact]
    public void CreateForActivePage_InvalidActivePageId_FallsBackToTheFirstPage()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "missing",
            Pages =
            [
                Page("first", new LabelWidgetConfig { Id = "l", Width = 200, Height = 40 }),
                Page("second", new LabelWidgetConfig { Id = "l2", Width = 200, Height = 40 }),
            ],
        };

        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());

        Assert.Equal("first", page.PageId);
    }

    [Fact]
    public void CreateForActivePage_MissingActivePageId_FallsBackToTheFirstPage()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = null,
            Pages = [Page("only", new LabelWidgetConfig { Id = "l", Width = 200, Height = 40 })],
        };

        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());

        Assert.Equal("only", page.PageId);
    }

    [Fact]
    public void Widgets_PreserveConfiguredGeometryInDipsAndConfigurationOrder()
    {
        var config = ConfigWith(
            new ButtonWidgetConfig
            {
                Id = "b1",
                X = 12.5,
                Y = 34,
                Width = 120.5,
                Height = 48,
                Action = new MuteActionConfig(),
            },
            new LabelWidgetConfig { Id = "l1", X = 200, Y = 10, Width = 300, Height = 40, Label = "Title" });

        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());

        Assert.Equal(2, page.Widgets.Count);

        var button = Assert.IsType<ButtonWidgetViewModel>(page.Widgets[0]);
        Assert.Equal("b1", button.WidgetId);
        Assert.Equal(12.5, button.X);
        Assert.Equal(34, button.Y);
        Assert.Equal(120.5, button.Width);
        Assert.Equal(48, button.Height);

        var label = Assert.IsType<LabelWidgetViewModel>(page.Widgets[1]);
        Assert.Equal("l1", label.WidgetId);
        Assert.Equal(200, label.X);
        Assert.Equal(300, label.Width);
        Assert.Equal("Title", label.Label);
    }

    [Fact]
    public void UnknownWidgetType_StaysInThePageAsADisabledPlaceholder()
    {
        var config = ConfigWith(new UnknownWidgetConfig("heatmap", ParseJson("{}")));
        ConfigValidator.Validate(config);

        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());

        var widget = Assert.IsType<UnsupportedWidgetViewModel>(Assert.Single(page.Widgets));
        Assert.False(widget.IsEnabled);
        Assert.Equal(WidgetTemplateKeys.Unsupported, widget.TemplateKey);
        Assert.False(string.IsNullOrWhiteSpace(widget.DisabledReason));
    }

    [Fact]
    public void InvalidActionBinding_KeepsTheTypeTemplateButIsDisabled()
    {
        var config = ConfigWith(new ButtonWidgetConfig
        {
            Id = "bad",
            Width = 80,
            Height = 40,
            Action = new UnknownActionConfig("http", ParseJson("{}")),
        });
        ConfigValidator.Validate(config);

        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());

        var widget = Assert.IsType<ButtonWidgetViewModel>(Assert.Single(page.Widgets));
        Assert.False(widget.IsEnabled);
        Assert.Equal(WidgetTemplateKeys.Button, widget.TemplateKey);
        Assert.False(string.IsNullOrWhiteSpace(widget.DisabledReason));
    }

    [Fact]
    public void ValidatedUndersizedWidget_KeepsTheFourtyDipTouchMinimum()
    {
        var config = ConfigWith(new ButtonWidgetConfig
        {
            Id = "tiny",
            X = 0,
            Y = 0,
            Width = 10,
            Height = 10,
            Action = new MuteActionConfig(),
        });
        ConfigValidator.Validate(config);

        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());

        var widget = Assert.Single(page.Widgets);
        Assert.Equal(ConfigValidator.MinWidgetSize, widget.Width);
        Assert.Equal(ConfigValidator.MinWidgetSize, widget.Height);
    }

    [Fact]
    public async Task PersistToggle_ReceivesThePageIdAndReadsAndWritesState()
    {
        var config = ConfigWith(new ToggleWidgetConfig
        {
            Id = "w1",
            Width = 96,
            Height = 48,
            PersistState = true,
            Action = new MuteActionConfig(),
        });

        var writes = new List<(string Page, string Widget, bool IsOn)>();
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext
        {
            ToggleStateLookup = (page, widget) => page == "main" && widget == "w1" ? true : null,
            ToggleStateChanged = (page, widget, isOn) => writes.Add((page, widget, isOn)),
        });

        var page = PageViewModel.CreateForActivePage(config, registry);
        var toggle = Assert.IsType<ToggleWidgetViewModel>(Assert.Single(page.Widgets));

        // Restored from the persisted state even though the configured initial state is false.
        Assert.True(toggle.IsOn);

        await toggle.ActivateAsync();
        Assert.Equal([("main", "w1", false)], writes);
    }

    [Fact]
    public void EmptyPageList_FallsBackToTheGeneratedDefaultPage()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            Pages = [],
        };

        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());

        Assert.Equal(DefaultConfigGenerator.DefaultPageId, page.PageId);
        Assert.NotEmpty(page.Widgets);
    }

    [Fact]
    public void SwappingTheActivePage_DisposesTheOutgoingPageSubscriptions()
    {
        // T-16/M2-RF-914: switching pages disposes the outgoing page's widget view models (releasing
        // their snapshot subscriptions) while the incoming page keeps following the source.
        var source = new FakeSnapshotSource();
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext { SnapshotSource = source });
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "first",
            Pages =
            [
                Page("first", Gauge("g1")),
                Page("second", Gauge("g2")),
            ],
        };

        var outgoing = PageViewModel.CreateForActivePage(config, registry);
        var outgoingGauge = Assert.IsType<GaugeWidgetViewModel>(Assert.Single(outgoing.Widgets));

        config.ActivePageId = "second";
        var incoming = PageViewModel.CreateForActivePage(config, registry);
        outgoing.Dispose();

        source.Publish(new SensorSnapshot { CapturedAt = DateTimeOffset.UnixEpoch, CpuTotalPercent = 77 });

        Assert.False(outgoingGauge.IsAvailable);
        Assert.True(Assert.IsType<GaugeWidgetViewModel>(Assert.Single(incoming.Widgets)).IsAvailable);
    }

    private static GaugeWidgetConfig Gauge(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 200,
        Height = 100,
        Sensor = SensorKeys.CpuTotal,
        Minimum = 0,
        Maximum = 100,
    };

    private static PanelConfig ConfigWith(params WidgetConfig[] widgets) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = "main",
        Pages = [Page("main", widgets)],
    };

    private static PageConfig Page(string id, params WidgetConfig[] widgets) => new()
    {
        Id = id,
        Widgets = [.. widgets],
    };

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
