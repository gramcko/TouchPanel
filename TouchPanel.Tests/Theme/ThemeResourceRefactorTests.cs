using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Monitoring;
using TouchPanel.Tests.Monitoring;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.Theme;

/// <summary>
/// M3-RF-851 / AC-M3-12 real-BAML coverage for the theme-resource refactor. Loads the real
/// <c>Theme/Colors.xaml</c> palette and the real compiled widget template dictionaries on an STA
/// thread, realizes widgets against the palette, swaps the palette at runtime (a stand-in for the
/// ThemeManager swap), and asserts:
/// <list type="bullet">
/// <item>every widget template still resolves its theme keys when the palette is present;</item>
/// <item>an override-less realized element's brush follows the live palette swap (proving the
/// DynamicResource conversion reaches already-realized elements);</item>
/// <item>a per-widget override still wins over the theme base while its non-overridden properties
/// keep following the theme (DD-M3-2 / RF-435/436 precedence).</item>
/// </list>
/// It also guards the source against a mechanical regression to <c>StaticResource</c> theme keys.
/// </summary>
public sealed class ThemeResourceRefactorTests
{
    private static readonly string[] PaletteBrushes =
    [
        "PanelBackgroundBrush",
        "PanelForegroundBrush",
        "PanelDisabledForegroundBrush",
        "PanelAccentBrush",
        "WidgetSurfaceBrush",
        "WidgetPressedBrush",
        "WidgetDisabledBrush",
        "WidgetBorderBrush",
        "WidgetErrorBrush",
    ];

    [Theory]
    [InlineData("Button")]
    [InlineData("Toggle")]
    [InlineData("Label")]
    [InlineData("Gauge")]
    [InlineData("Slider")]
    [InlineData("Sparkline")]
    [InlineData("Clock")]
    [InlineData("ImageButton")]
    [InlineData("Bar")]
    [InlineData("Unsupported")]
    public async Task WidgetTemplate_ResolvesThemeKeysAgainstTheRealPalette(string kind)
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var palette = LoadPalette();
            var (template, viewModel) = CreateTemplateAndViewModel(kind);

            var presenter = Realize(template, viewModel, palette, out _);

            // The DynamicResource carrier (`Tag`) proves the palette key resolved for a realized
            // element. Every interactive/display template carries the surface (or foreground) key.
            var expectedKey = kind is "Label" ? "PanelForegroundBrush"
                : kind is "Unsupported" ? null
                : "WidgetSurfaceBrush";

            if (expectedKey is null)
            {
                // Unsupported uses direct DynamicResource backgrounds rather than an override tag.
                var border = FindDescendant<Border>(presenter, b => b.Background is SolidColorBrush);
                Assert.NotNull(border);
                var color = Assert.IsType<SolidColorBrush>(border!.Background).Color;
                Assert.Equal(Assert.IsType<SolidColorBrush>(palette["WidgetDisabledBrush"]).Color, color);
            }
            else
            {
                var tagged = FindDescendant<FrameworkElement>(presenter, e => e.Tag is SolidColorBrush);
                Assert.NotNull(tagged);
                Assert.Same(palette[expectedKey], tagged!.Tag);
            }
        });
    }

    [Fact]
    public async Task RealizedWidget_FollowsLiveThemeSwap_AndPerWidgetOverrideStillWins()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var palette = LoadPalette();
            var template = LoadTemplate("/TouchPanel;component/Widgets/ButtonWidget/ButtonWidgetTemplates.xaml", "Widget.Button");

            using var plainVm = new ButtonWidgetViewModel(new ButtonWidgetConfig
            {
                Id = "plain",
                X = 0,
                Y = 0,
                Width = 160,
                Height = 60,
                Label = "Plain",
                Action = new MuteActionConfig(),
            });

            const byte overrideRed = 0x00;
            const byte overrideGreen = 0xFF;
            const byte overrideBlue = 0x00;
            using var overriddenVm = new ButtonWidgetViewModel(new ButtonWidgetConfig
            {
                Id = "overridden",
                X = 0,
                Y = 0,
                Width = 160,
                Height = 60,
                Label = "Overridden",
                Background = "#FF00FF00",
                Action = new MuteActionConfig(),
            });

            var plainHost = new Grid();
            plainHost.Resources.MergedDictionaries.Add(palette);
            var plainPresenter = RealizeInto(plainHost, template, plainVm, 160, 60);
            var plainButton = Assert.IsType<Button>(FindDescendant<Button>(plainPresenter, _ => true));
            var overriddenPresenter = RealizeInto(plainHost, template, overriddenVm, 160, 60);
            var overriddenButton = Assert.IsType<Button>(FindDescendant<Button>(overriddenPresenter, _ => true));

            var themeSurfaceColor = Assert.IsType<SolidColorBrush>(palette["WidgetSurfaceBrush"]).Color;
            Assert.Equal(themeSurfaceColor, Assert.IsType<SolidColorBrush>(plainButton.Background).Color);
            Assert.Equal(
                Color.FromRgb(overrideRed, overrideGreen, overrideBlue),
                Assert.IsType<SolidColorBrush>(overriddenButton.Background).Color);

            // Stand-in for a live ThemeManager swap: replace the palette entry in place, exactly as
            // swapping the active theme dictionary does. The DynamicResource carrier re-resolves.
            var swappedColor = Color.FromRgb(0x11, 0x22, 0x33);
            palette["WidgetSurfaceBrush"] = new SolidColorBrush(swappedColor);
            plainHost.UpdateLayout();

            Assert.Equal(swappedColor, Assert.IsType<SolidColorBrush>(plainButton.Background).Color);

            // The overridden widget keeps its override; the theme base never leaks over it.
            Assert.Equal(
                Color.FromRgb(overrideRed, overrideGreen, overrideBlue),
                Assert.IsType<SolidColorBrush>(overriddenButton.Background).Color);
        });
    }

    [Fact]
    public void ThemeSource_HasNoStaticPaletteReferences()
    {
        var root = FindRepositoryRoot();
        var paletteKeyPattern = new Regex(
            @"StaticResource\s+(?:" + string.Join('|', PaletteBrushes) + ")",
            RegexOptions.CultureInvariant);

        var files = new List<string>
        {
            Path.Combine(root, "TouchPanel", "Theme", "Styles.xaml"),
        };
        files.AddRange(Directory.EnumerateFiles(
            Path.Combine(root, "TouchPanel", "Widgets"),
            "*Templates.xaml",
            SearchOption.AllDirectories));
        files.Add(Path.Combine(root, "TouchPanel", "Views", "EditOverlay.xaml"));
        files.Add(Path.Combine(root, "TouchPanel", "Views", "PageSwitcher", "PageSwitcherView.xaml"));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.False(
                paletteKeyPattern.IsMatch(text),
                $"{Path.GetFileName(file)} still binds a palette key with StaticResource");
            Assert.DoesNotContain("TargetNullValue={StaticResource", text, StringComparison.Ordinal);
        }

        // The palette must be merged at the application level and never self-merged by Styles.xaml.
        var styles = File.ReadAllText(Path.Combine(root, "TouchPanel", "Theme", "Styles.xaml"));
        Assert.DoesNotContain("Colors.xaml", styles, StringComparison.Ordinal);
        var app = File.ReadAllText(Path.Combine(root, "TouchPanel", "App.xaml"));
        Assert.Contains("Theme/Themes/Dark.xaml", app, StringComparison.Ordinal);
    }

    private static ResourceDictionary LoadPalette() =>
        Assert.IsType<ResourceDictionary>(Application.LoadComponent(
            new Uri("/TouchPanel;component/Theme/Colors.xaml", UriKind.Relative)));

    private static (DataTemplate Template, WidgetViewModelBase ViewModel) CreateTemplateAndViewModel(string kind)
    {
        switch (kind)
        {
            case "Button":
                var buttonTemplate = LoadTemplate(
                    "/TouchPanel;component/Widgets/ButtonWidget/ButtonWidgetTemplates.xaml", "Widget.Button");
                return (buttonTemplate, new ButtonWidgetViewModel(new ButtonWidgetConfig
                {
                    Id = "b1",
                    X = 0,
                    Y = 0,
                    Width = 160,
                    Height = 60,
                    Label = "Go",
                    Action = new MuteActionConfig(),
                }));

            case "Toggle":
                var toggleTemplate = LoadTemplate(
                    "/TouchPanel;component/Widgets/ToggleWidget/ToggleWidgetTemplates.xaml", "Widget.Toggle");
                return (toggleTemplate, new ToggleWidgetViewModel(new ToggleWidgetConfig
                {
                    Id = "t1",
                    X = 0,
                    Y = 0,
                    Width = 160,
                    Height = 60,
                    Label = "Toggle",
                }));

            case "Label":
                var labelTemplate = LoadTemplate(
                    "/TouchPanel;component/Widgets/LabelWidget/LabelWidgetTemplates.xaml", "Widget.Label");
                return (labelTemplate, new LabelWidgetViewModel(new LabelWidgetConfig
                {
                    Id = "l1",
                    X = 0,
                    Y = 0,
                    Width = 200,
                    Height = 60,
                    Label = "Hello",
                }));

            case "Gauge":
                var gaugeTemplate = LoadTemplate(
                    "/TouchPanel;component/Widgets/GaugeWidget/GaugeWidgetTemplates.xaml", "Widget.Gauge");
                var source = new FakeSnapshotSource();
                source.Publish(new SensorSnapshot { CapturedAt = DateTimeOffset.UnixEpoch, CpuTotalPercent = 42 });
                return (gaugeTemplate, new GaugeWidgetViewModel(new GaugeWidgetConfig
                {
                    Id = "g1",
                    X = 0,
                    Y = 0,
                    Width = 200,
                    Height = 100,
                    Label = "CPU",
                    Sensor = SensorKeys.CpuTotal,
                    Minimum = 0,
                    Maximum = 100,
                }, source));

            case "Slider":
                var sliderTemplate = LoadTemplate(
                    "/TouchPanel;component/Widgets/SliderWidget/SliderWidgetTemplates.xaml", "Widget.Slider");
                return (sliderTemplate, new SliderWidgetViewModel(new SliderWidgetConfig
                {
                    Id = "s1",
                    X = 0,
                    Y = 0,
                    Width = 200,
                    Height = 100,
                    Label = "Volume",
                    Minimum = 0,
                    Maximum = 100,
                    Step = 1,
                    Value = 30,
                    Action = new VolumeActionConfig { Mode = VolumeMode.Set },
                }));

            case "Sparkline":
                var sparklineTemplate = LoadTemplate(
                    "/TouchPanel;component/Widgets/SparklineWidget/SparklineWidgetTemplates.xaml", "Widget.Sparkline");
                var history = new FakeSensorHistory();
                history.Set(SensorKeys.CpuTotal, 1, null, 3);
                return (sparklineTemplate, new SparklineWidgetViewModel(new SparklineWidgetConfig
                {
                    Id = "sp1",
                    X = 0,
                    Y = 0,
                    Width = 240,
                    Height = 100,
                    Label = "CPU trend",
                    Sensor = SensorKeys.CpuTotal,
                    WindowSeconds = 60,
                    Style = SparklineStyle.Filled,
                }, history));

            case "Clock":
                var clockTemplate = LoadTemplate(
                    "/TouchPanel;component/Widgets/ClockWidget/ClockWidgetTemplates.xaml", "Widget.Clock");
                return (clockTemplate, new ClockWidgetViewModel(new ClockWidgetConfig
                {
                    Id = "c1",
                    X = 0,
                    Y = 0,
                    Width = 240,
                    Height = 120,
                    Label = "Time",
                    Mode = ClockMode.Time24,
                }, timeProvider: new FakeTimeProvider(), culture: CultureInfo.InvariantCulture, timeZone: TimeZoneInfo.Utc));

            case "ImageButton":
                var imageButtonTemplate = LoadTemplate(
                    "/TouchPanel;component/Widgets/ImageButtonWidget/ImageButtonWidgetTemplates.xaml", "Widget.ImageButton");
                return (imageButtonTemplate, new ImageButtonWidgetViewModel(new ImageButtonWidgetConfig
                {
                    Id = "ib1",
                    X = 0,
                    Y = 0,
                    Width = 120,
                    Height = 120,
                    Label = "Go",
                    Icon = "power",
                    ShowLabel = true,
                    Action = new MuteActionConfig(),
                }));

            case "Bar":
                var barTemplate = LoadTemplate(
                    "/TouchPanel;component/Widgets/BarWidget/BarWidgetTemplates.xaml", "Widget.Bar");
                var barSource = new FakeSnapshotSource();
                barSource.Publish(new SensorSnapshot { CapturedAt = DateTimeOffset.UnixEpoch, CpuTotalPercent = 42 });
                return (barTemplate, new BarWidgetViewModel(new BarWidgetConfig
                {
                    Id = "bar1",
                    X = 0,
                    Y = 0,
                    Width = 240,
                    Height = 64,
                    Label = "CPU",
                    Sensor = SensorKeys.CpuTotal,
                    Minimum = 0,
                    Maximum = 100,
                    ShowValue = true,
                }, barSource));

            default:
                var unsupportedTemplate = LoadTemplate(
                    "/TouchPanel;component/Widgets/UnsupportedWidget/UnsupportedWidgetTemplates.xaml", "Widget.Unsupported");
                return (unsupportedTemplate, new UnsupportedWidgetViewModel(new LabelWidgetConfig
                {
                    Id = "u1",
                    X = 0,
                    Y = 0,
                    Width = 160,
                    Height = 60,
                }, "unknown type"));
        }
    }

    private static DataTemplate LoadTemplate(string componentUri, string key)
    {
        var dictionary = Assert.IsType<ResourceDictionary>(Application.LoadComponent(
            new Uri(componentUri, UriKind.Relative)));
        return Assert.IsType<DataTemplate>(dictionary[key]);
    }

    private static ContentPresenter Realize(
        DataTemplate template,
        object content,
        ResourceDictionary palette,
        out Grid host)
    {
        host = new Grid();
        host.Resources.MergedDictionaries.Add(palette);
        return RealizeInto(host, template, content, 240, 120);
    }

    private static ContentPresenter RealizeInto(Grid host, DataTemplate template, object content, double width, double height)
    {
        var presenter = new ContentPresenter
        {
            Content = content,
            ContentTemplate = template,
        };

        host.Children.Add(presenter);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        return presenter;
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        if (root is T match && predicate(match))
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindDescendant(VisualTreeHelper.GetChild(root, i), predicate) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TouchPanel.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
