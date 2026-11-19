using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.Views;

/// <summary>
/// User-requested addition: the optional label icon must replace the label text in the compiled
/// templates. These tests load the real BAML resource dictionaries, materialize the template with
/// and without <see cref="WidgetConfig.LabelIcon"/>, and assert the icon is shown (with the text
/// collapsed) only when one is configured — the reverse by default, for the Bar and the Gauge.
/// </summary>
public sealed class WidgetLabelIconTemplateTests
{
    [Fact]
    public async Task BarTemplate_ShowsTheLabelIconInsteadOfTheTextOnlyWhenSet()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var template = LoadTemplate(
                "/TouchPanel;component/Widgets/BarWidget/BarWidgetTemplates.xaml",
                "Widget.Bar");

            var plain = Materialize(template, new BarWidgetConfig
            {
                Id = "bar-1",
                X = 0,
                Y = 0,
                Width = 240,
                Height = 64,
                Label = "Download",
                Sensor = "net.down",
            });

            var textDefault = FindByName<TextBlock>(plain, "BarLabel");
            var iconDefault = FindByName<TextBlock>(plain, "BarLabelIcon");
            Assert.NotNull(textDefault);
            Assert.NotNull(iconDefault);
            Assert.Equal("Download", textDefault!.Text);
            Assert.Equal(Visibility.Visible, textDefault.Visibility);
            Assert.Equal(Visibility.Collapsed, iconDefault!.Visibility);

            var withIcon = Materialize(template, new BarWidgetConfig
            {
                Id = "bar-2",
                X = 0,
                Y = 0,
                Width = 240,
                Height = 64,
                Label = "Download",
                LabelIcon = "download",
                Sensor = "net.down",
            });

            var textWithIcon = FindByName<TextBlock>(withIcon, "BarLabel");
            var iconWithIcon = FindByName<TextBlock>(withIcon, "BarLabelIcon");
            Assert.NotNull(textWithIcon);
            Assert.NotNull(iconWithIcon);
            Assert.Equal(Visibility.Collapsed, textWithIcon!.Visibility);
            Assert.Equal(Visibility.Visible, iconWithIcon!.Visibility);
            Assert.Equal("\uE896", iconWithIcon.Text);
            Assert.Equal("Segoe MDL2 Assets", iconWithIcon.FontFamily.Source);
            Assert.Equal("Download", iconWithIcon.ToolTip);
        });
    }

    [Fact]
    public async Task GaugeTemplate_ShowsTheLabelIconInsteadOfTheTextOnlyWhenSet()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var template = LoadTemplate(
                "/TouchPanel;component/Widgets/GaugeWidget/GaugeWidgetTemplates.xaml",
                "Widget.Gauge");

            var plain = Materialize(template, new GaugeWidgetConfig
            {
                Id = "gauge-1",
                X = 0,
                Y = 0,
                Width = 200,
                Height = 120,
                Label = "Download",
                Sensor = "net.down",
                Minimum = 0,
                Maximum = 12500000,
            });

            var textDefault = FindByName<TextBlock>(plain, "GaugeLabel");
            var iconDefault = FindByName<TextBlock>(plain, "LabelIconGlyph");
            Assert.NotNull(textDefault);
            Assert.NotNull(iconDefault);
            Assert.Equal(Visibility.Visible, textDefault!.Visibility);
            Assert.Equal(Visibility.Collapsed, iconDefault!.Visibility);

            var withIcon = Materialize(template, new GaugeWidgetConfig
            {
                Id = "gauge-2",
                X = 0,
                Y = 0,
                Width = 200,
                Height = 120,
                Label = "Download",
                LabelIcon = "download",
                Sensor = "net.down",
                Minimum = 0,
                Maximum = 12500000,
            });

            var textWithIcon = FindByName<TextBlock>(withIcon, "GaugeLabel");
            var iconWithIcon = FindByName<TextBlock>(withIcon, "LabelIconGlyph");
            Assert.NotNull(textWithIcon);
            Assert.NotNull(iconWithIcon);
            Assert.Equal(Visibility.Collapsed, textWithIcon!.Visibility);
            Assert.Equal(Visibility.Visible, iconWithIcon!.Visibility);
            Assert.Equal("\uE896", iconWithIcon.Text);
            Assert.Equal("Download", iconWithIcon.ToolTip);
        });
    }

    private static ContentPresenter Materialize(DataTemplate template, WidgetConfig config)
    {
        WidgetViewModelBase viewModel = config switch
        {
            BarWidgetConfig bar => new BarWidgetViewModel(bar),
            GaugeWidgetConfig gauge => new GaugeWidgetViewModel(gauge),
            _ => throw new ArgumentOutOfRangeException(nameof(config)),
        };

        var presenter = new ContentPresenter
        {
            Content = viewModel,
            ContentTemplate = template,
        };

        presenter.Measure(new Size(config.Width, config.Height));
        presenter.Arrange(new Rect(0, 0, config.Width, config.Height));
        presenter.UpdateLayout();
        return presenter;
    }

    private static DataTemplate LoadTemplate(string uri, string key)
    {
        var dictionary = Assert.IsType<ResourceDictionary>(Application.LoadComponent(
            new Uri(uri, UriKind.Relative)));
        return Assert.IsType<DataTemplate>(dictionary[key]);
    }

    private static T? FindByName<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        if (root is T element && string.Equals(element.Name, name, StringComparison.Ordinal))
        {
            return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindByName<T>(VisualTreeHelper.GetChild(root, i), name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
