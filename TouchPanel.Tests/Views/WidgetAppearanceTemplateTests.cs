using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Button template appearance smoke test (T-14 part 2, RF-043/M2-RF-435/M2-RF-436): loads the real
/// resource dictionary and materializes the <c>Widget.Button</c> DataTemplate against a view model
/// configured with a built-in icon, a background override, and an opacity override. This proves the
/// template renders the icon and layers the appearance overrides without a template/binding defect.
/// </summary>
public sealed class WidgetAppearanceTemplateTests
{
    [Fact]
    public async Task ButtonTemplate_RendersIconAndAppearanceOverrides()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var dictionary = Assert.IsType<ResourceDictionary>(Application.LoadComponent(
                new Uri(
                    "/TouchPanel;component/Widgets/ButtonWidget/ButtonWidgetTemplates.xaml",
                    UriKind.Relative)));
            var template = Assert.IsType<DataTemplate>(dictionary["Widget.Button"]);

            using var button = new ButtonWidgetViewModel(new ButtonWidgetConfig
            {
                Id = "b1",
                X = 0,
                Y = 0,
                Width = 200,
                Height = 100,
                Label = "Go",
                Icon = "power",
                Background = "#FF112233",
                Opacity = 0.6,
                Action = new MuteActionConfig(),
            });

            var presenter = new ContentPresenter
            {
                Content = button,
                ContentTemplate = template,
            };

            presenter.Measure(new Size(200, 100));
            presenter.Arrange(new Rect(0, 0, 200, 100));
            presenter.UpdateLayout();

            var icon = FindDescendant<TextBlock>(
                presenter,
                block => string.Equals(block.FontFamily.Source, "Segoe MDL2 Assets", StringComparison.Ordinal));
            Assert.NotNull(icon);
            Assert.Equal(IconCatalog.GlyphFor("power"), icon!.Text);
            Assert.Equal(Visibility.Visible, icon.Visibility);

            var surface = FindDescendant<Button>(presenter, _ => true);
            Assert.NotNull(surface);
            var background = Assert.IsType<SolidColorBrush>(surface!.Background);
            Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), background.Color);

            var root = Assert.IsAssignableFrom<FrameworkElement>(VisualTreeHelper.GetChild(presenter, 0));
            Assert.Equal(0.6, root.Opacity, precision: 3);
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
