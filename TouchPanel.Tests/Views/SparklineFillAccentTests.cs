using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Monitoring;
using TouchPanel.Tests.Monitoring;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Sparkline filled-area accent behavior (M4-RF-950/D-M4-4, AC-M4-03). Unit-tests the fill converter
/// precedence/alpha/fallback without WPF, then loads the real
/// <c>SparklineWidgetTemplates.xaml</c> on an STA thread, realizes filled Sparklines against the real
/// palette, and asserts the filled area follows the global accent, tracks a live palette swap, lets a
/// per-widget accent override win, and falls back without throwing. A source guard proves the old
/// hard-coded <c>#553D7EFF</c> literal is gone.
/// </summary>
public sealed class SparklineFillAccentTests
{
    private static readonly Color ThemeAccent = Color.FromRgb(0x3D, 0x7E, 0xFF);

    [Fact]
    public void Converter_PrefersTheOverride_AndAppliesTheDocumentedAlpha()
    {
        var converter = new AppearanceAccentFillConverter();
        var fallback = new SolidColorBrush(ThemeAccent);
        var over = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00));

        var result = Assert.IsType<SolidColorBrush>(converter.Convert(
            [over, fallback],
            typeof(Brush),
            null,
            System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(AppearanceAccentFillConverter.DefaultFillAlpha, result.Color.A);
        Assert.Equal(Color.FromRgb(0x00, 0x80, 0x00), Color.FromRgb(result.Color.R, result.Color.G, result.Color.B));
    }

    [Fact]
    public void Converter_UsesTheFallbackAccent_WhenNoOverrideIsPresent()
    {
        var converter = new AppearanceAccentFillConverter();
        var fallback = new SolidColorBrush(ThemeAccent);

        var result = Assert.IsType<SolidColorBrush>(converter.Convert(
            [null!, fallback],
            typeof(Brush),
            null,
            System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(AppearanceAccentFillConverter.DefaultFillAlpha, result.Color.A);
        Assert.Equal(ThemeAccent, Color.FromRgb(result.Color.R, result.Color.G, result.Color.B));
    }

    [Fact]
    public void Converter_ReturnsUnsetValue_WhenNoAccentIsAvailable_AndNeverThrows()
    {
        var converter = new AppearanceAccentFillConverter();

        Assert.Same(
            DependencyProperty.UnsetValue,
            converter.Convert([null!, null!], typeof(Brush), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Same(
            DependencyProperty.UnsetValue,
            converter.Convert([], typeof(Brush), null, System.Globalization.CultureInfo.InvariantCulture));

        // Hostile input must not throw either.
        Assert.Same(
            DependencyProperty.UnsetValue,
            converter.Convert(["not a brush", 42], typeof(Brush), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task RealTemplate_FillFollowsTheGlobalAccent_AndSurvivesALiveSwap_OverrideWins()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var palette = Assert.IsType<ResourceDictionary>(Application.LoadComponent(
                new Uri("/TouchPanel;component/Theme/Colors.xaml", UriKind.Relative)));

            var template = Assert.IsType<DataTemplate>(Assert.IsType<ResourceDictionary>(Application.LoadComponent(
                new Uri(
                    "/TouchPanel;component/Widgets/SparklineWidget/SparklineWidgetTemplates.xaml",
                    UriKind.Relative)))["Widget.Sparkline"]);

            var host = new Grid();
            host.Resources.MergedDictionaries.Add(palette);

            var plain = Realize(host, template, CreateFilledViewModel("plain", accent: null));
            var overridden = Realize(host, template, CreateFilledViewModel("overridden", accent: "#FF008000"));

            var plainPlot = Assert.IsType<SparklinePlot>(FindDescendant<SparklinePlot>(plain));
            var overriddenPlot = Assert.IsType<SparklinePlot>(FindDescendant<SparklinePlot>(overridden));

            var themeColor = Assert.IsType<SolidColorBrush>(palette["PanelAccentBrush"]).Color;
            AssertFill(plainPlot, themeColor);

            // A per-widget accent override wins over the theme accent (RF-436 precedence).
            AssertFill(overriddenPlot, Color.FromRgb(0x00, 0x80, 0x00));

            // Stand-in for a live ThemeManager swap: the DynamicResource carrier re-resolves.
            var swapped = Color.FromRgb(0x11, 0x22, 0x33);
            palette["PanelAccentBrush"] = new SolidColorBrush(swapped);
            host.UpdateLayout();

            AssertFill(plainPlot, swapped);
            AssertFill(overriddenPlot, Color.FromRgb(0x00, 0x80, 0x00));
        });
    }

    [Fact]
    public async Task RealTemplate_WithoutAnyAccent_FallsBackWithoutThrowing()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var template = Assert.IsType<DataTemplate>(Assert.IsType<ResourceDictionary>(Application.LoadComponent(
                new Uri(
                    "/TouchPanel;component/Widgets/SparklineWidget/SparklineWidgetTemplates.xaml",
                    UriKind.Relative)))["Widget.Sparkline"]);

            // No palette is merged and the view model carries no override: the fill binding resolves
            // to UnsetValue and the plot keeps its neutral default rather than crashing.
            var host = new Grid();
            var presenter = Realize(host, template, CreateFilledViewModel("no-accent", accent: null));
            var plot = Assert.IsType<SparklinePlot>(FindDescendant<SparklinePlot>(presenter));

            Assert.Null(plot.FillBrush);
            Assert.True(plot.IsFilled);
        });
    }

    [Fact]
    public void SparklineTemplate_SourceHasNoHardCodedFillLiteral()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "TouchPanel",
            "Widgets",
            "SparklineWidget",
            "SparklineWidgetTemplates.xaml");
        var text = File.ReadAllText(path);

        Assert.DoesNotContain("553D7EFF", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SparklineFillBrush", text, StringComparison.Ordinal);
        Assert.Contains("AppearanceAccentFillConverter", text, StringComparison.Ordinal);
    }

    private static void AssertFill(SparklinePlot plot, Color expectedRgb)
    {
        Assert.True(plot.IsFilled);
        var brush = Assert.IsType<SolidColorBrush>(plot.FillBrush);
        Assert.Equal(AppearanceAccentFillConverter.DefaultFillAlpha, brush.Color.A);
        Assert.Equal(expectedRgb, Color.FromRgb(brush.Color.R, brush.Color.G, brush.Color.B));
    }

    private static SparklineWidgetViewModel CreateFilledViewModel(string id, string? accent)
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 1, null, 3);
        return new SparklineWidgetViewModel(
            new SparklineWidgetConfig
            {
                Id = id,
                X = 0,
                Y = 0,
                Width = 240,
                Height = 100,
                Label = id,
                Sensor = SensorKeys.CpuTotal,
                WindowSeconds = 60,
                Style = SparklineStyle.Filled,
                Accent = accent,
            },
            history);
    }

    private static ContentPresenter Realize(Grid host, DataTemplate template, object content)
    {
        var presenter = new ContentPresenter
        {
            Content = content,
            ContentTemplate = template,
        };

        host.Children.Add(presenter);
        host.Measure(new Size(240, 100));
        host.Arrange(new Rect(0, 0, 240, 100));
        host.UpdateLayout();
        return presenter;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindDescendant<T>(VisualTreeHelper.GetChild(root, i)) is { } found)
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
