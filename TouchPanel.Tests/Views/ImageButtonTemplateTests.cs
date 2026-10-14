using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.Views;

/// <summary>
/// ImageButton template smoke test (RF-046/M3-RF-464/465): loads the real compiled BAML dictionary,
/// realizes the template against a live view model on an STA thread, and asserts the interactive
/// surface is non-activating (<c>Focusable="False"</c>) so a tap never steals keyboard focus from
/// another application (RF-018/RF-025).
/// </summary>
public sealed class ImageButtonTemplateTests
{
    [Fact]
    public async Task ImageButtonTemplate_LoadsAndItsSurfaceIsNonActivating()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var dictionary = Assert.IsType<ResourceDictionary>(Application.LoadComponent(
                new Uri(
                    "/TouchPanel;component/Widgets/ImageButtonWidget/ImageButtonWidgetTemplates.xaml",
                    UriKind.Relative)));
            var template = Assert.IsType<DataTemplate>(dictionary["Widget.ImageButton"]);

            using var viewModel = new ImageButtonWidgetViewModel(new ImageButtonWidgetConfig
            {
                Id = "ib-1",
                X = 0,
                Y = 0,
                Width = 120,
                Height = 120,
                Label = "Go",
                Icon = "power",
                ShowLabel = true,
                Action = new MuteActionConfig(),
            });

            var presenter = new ContentPresenter
            {
                Content = viewModel,
                ContentTemplate = template,
            };

            presenter.Measure(new Size(120, 120));
            presenter.Arrange(new Rect(0, 0, 120, 120));

            var button = Assert.IsType<Button>(FindDescendant<Button>(presenter, _ => true));
            Assert.False(button.Focusable);
        });
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
}
