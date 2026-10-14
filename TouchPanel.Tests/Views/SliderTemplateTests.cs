using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Slider template smoke test (T-06, RF-040/M2-RF-405): loads the real resource dictionary and
/// applies the <c>Widget.Slider</c> <see cref="DataTemplate"/> to a live view model on an STA
/// thread, forcing template instantiation, binding attachment, measure, and arrange. This catches
/// template/binding defects that pure view-model tests cannot see and proves the touch control
/// receives the configured range/value.
/// </summary>
public sealed class SliderTemplateTests
{
    [Fact]
    public async Task SliderTemplate_LoadsAndBindsToTheViewModel()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var dictionary = Assert.IsType<ResourceDictionary>(Application.LoadComponent(
                new Uri(
                    "/TouchPanel;component/Widgets/SliderWidget/SliderWidgetTemplates.xaml",
                    UriKind.Relative)));
            var template = Assert.IsType<DataTemplate>(dictionary["Widget.Slider"]);

            using var slider = new SliderWidgetViewModel(new SliderWidgetConfig
            {
                Id = "slider-1",
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
            });

            var presenter = new ContentPresenter
            {
                Content = slider,
                ContentTemplate = template,
            };

            // Materializing the template attaches the two-way Value binding; this must complete
            // without a XamlParseException.
            presenter.Measure(new Size(200, 100));
            presenter.Arrange(new Rect(0, 0, 200, 100));
            presenter.UpdateLayout();

            var touchSlider = FindDescendant<TouchSlider>(presenter);
            Assert.NotNull(touchSlider);
            Assert.Equal(0d, touchSlider!.Minimum);
            Assert.Equal(100d, touchSlider.Maximum);
            Assert.Equal(30d, touchSlider.Value);
        });
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
