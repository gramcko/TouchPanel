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
/// Sparkline template smoke test (T-07, RF-041/M2-RF-411/413): loads the real resource dictionary
/// and applies the <c>Widget.Sparkline</c> <see cref="DataTemplate"/> to a live view model on an STA
/// thread, forcing template instantiation, binding attachment, measure, and arrange. This catches
/// template/binding defects pure view-model tests cannot see and proves the plot receives the
/// history-derived points and resolved scale.
/// </summary>
public sealed class SparklineTemplateTests
{
    [Fact]
    public async Task SparklineTemplate_LoadsAndBindsThePlotToTheViewModel()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var dictionary = Assert.IsType<ResourceDictionary>(Application.LoadComponent(
                new Uri(
                    "/TouchPanel;component/Widgets/SparklineWidget/SparklineWidgetTemplates.xaml",
                    UriKind.Relative)));
            var template = Assert.IsType<DataTemplate>(dictionary["Widget.Sparkline"]);

            var history = new FakeSensorHistory();
            history.Set(SensorKeys.CpuTotal, 1, null, 3);

            using var viewModel = new SparklineWidgetViewModel(
                new SparklineWidgetConfig
                {
                    Id = "spark-1",
                    X = 0,
                    Y = 0,
                    Width = 240,
                    Height = 100,
                    Label = "CPU trend",
                    Sensor = SensorKeys.CpuTotal,
                    WindowSeconds = 60,
                    Style = SparklineStyle.Filled,
                },
                history);

            var presenter = new ContentPresenter
            {
                Content = viewModel,
                ContentTemplate = template,
            };

            presenter.Measure(new Size(240, 100));
            presenter.Arrange(new Rect(0, 0, 240, 100));
            presenter.UpdateLayout();

            var plot = FindDescendant<SparklinePlot>(presenter);
            Assert.NotNull(plot);
            Assert.Equal(new double?[] { 1, null, 3 }, plot!.Points);
            Assert.Equal(1, plot.ScaleMinimum);
            Assert.Equal(3, plot.ScaleMaximum);
            Assert.True(plot.IsFilled);
        });
    }

    [Fact]
    public async Task SparklineTemplate_ShowsTheCurrentValueOnlyWhenEnabled()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var dictionary = Assert.IsType<ResourceDictionary>(Application.LoadComponent(
                new Uri(
                    "/TouchPanel;component/Widgets/SparklineWidget/SparklineWidgetTemplates.xaml",
                    UriKind.Relative)));
            var template = Assert.IsType<DataTemplate>(dictionary["Widget.Sparkline"]);

            // ShowValue = true → the header readout is present and visible.
            var enabled = CreatePresenter(template, showValue: true);
            var enabledText = FindTextBlock(enabled, "3 %");
            Assert.NotNull(enabledText);
            Assert.Equal(Visibility.Visible, enabledText!.Visibility);

            // ShowValue = false (the default) → the readout is collapsed (graph only).
            var disabled = CreatePresenter(template, showValue: false);
            var disabledText = FindTextBlock(disabled, "3 %");
            Assert.NotNull(disabledText);
            Assert.Equal(Visibility.Collapsed, disabledText!.Visibility);
        });
    }

    private static ContentPresenter CreatePresenter(DataTemplate template, bool showValue)
    {
        var history = new FakeSensorHistory();
        history.Set(SensorKeys.CpuTotal, 1, null, 3);

        var viewModel = new SparklineWidgetViewModel(
            new SparklineWidgetConfig
            {
                Id = "spark-1",
                X = 0,
                Y = 0,
                Width = 240,
                Height = 100,
                Label = "CPU trend",
                Sensor = SensorKeys.CpuTotal,
                WindowSeconds = 60,
                Style = SparklineStyle.Line,
                ShowValue = showValue,
            },
            history);

        var presenter = new ContentPresenter
        {
            Content = viewModel,
            ContentTemplate = template,
        };

        presenter.Measure(new Size(240, 100));
        presenter.Arrange(new Rect(0, 0, 240, 100));
        presenter.UpdateLayout();
        return presenter;
    }

    private static TextBlock? FindTextBlock(DependencyObject root, string text)
    {
        if (root is TextBlock textBlock && string.Equals(textBlock.Text, text, StringComparison.Ordinal))
        {
            return textBlock;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindTextBlock(VisualTreeHelper.GetChild(root, i), text) is { } found)
            {
                return found;
            }
        }

        return null;
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
}
