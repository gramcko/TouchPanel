using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Image template smoke test (user-requested addition): loads the real compiled BAML dictionary,
/// realizes the template against a live view model on an STA thread, and asserts the display-only
/// surface is non-hit-test and shows the explicit placeholder (never a blank) while no image is
/// available.
/// </summary>
public sealed class ImageTemplateTests
{
    [Fact]
    public async Task ImageTemplate_LoadsAndShowsThePlaceholderWhenUnavailable()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var dictionary = Assert.IsType<ResourceDictionary>(Application.LoadComponent(
                new Uri(
                    "/TouchPanel;component/Widgets/ImageWidget/ImageWidgetTemplates.xaml",
                    UriKind.Relative)));
            var template = Assert.IsType<DataTemplate>(dictionary[WidgetTemplateKeys.Image]);

            // Display-only: the root surface is not hit-testable, so a tap never reaches it.
            var root = Assert.IsType<Border>(template.LoadContent());
            Assert.False(root.IsHitTestVisible);

            using var viewModel = new ImageWidgetViewModel(new ImageWidgetConfig
            {
                Id = "img-1",
                X = 0,
                Y = 0,
                Width = 240,
                Height = 160,
            });

            var presenter = new ContentPresenter
            {
                Content = viewModel,
                ContentTemplate = template,
            };

            presenter.Measure(new Size(240, 160));
            presenter.Arrange(new Rect(0, 0, 240, 160));

            var image = Assert.IsType<Image>(FindDescendant<Image>(presenter, _ => true));
            var placeholder = Assert.IsType<TextBlock>(FindDescendant<TextBlock>(
                presenter,
                text => text.Text == ImageWidgetViewModel.PlaceholderText));

            Assert.Equal(Visibility.Collapsed, image.Visibility);
            Assert.Equal(Visibility.Visible, placeholder.Visibility);
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
