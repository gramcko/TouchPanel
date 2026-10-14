using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Regression coverage for the toggle label visibility defect: the accent <c>OnLayer</c> background
/// must be a background-only sibling of the content presenter, not its (opacity-animated) parent, so
/// the label stays visible in the off, on, and disabled states. Loads the real compiled BAML toggle
/// template on an STA thread; must fail on the old nested structure.
/// </summary>
public sealed class ToggleTemplateContentVisibilityTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ToggleTemplate_ContentPresenterIsOutsideTheOnLayerAndEffectivelyVisible(bool isOn, bool isEnabled)
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewModel = new ToggleWidgetViewModel(new ToggleWidgetConfig
            {
                Id = "toggle-1",
                X = 0,
                Y = 0,
                Width = 120,
                Height = 60,
                Label = "Mute",
                InitialOn = isOn,
                IsEnabled = isEnabled,
            });

            var presenter = Materialize(viewModel);

            var onLayer = Assert.IsType<Border>(FindByName(presenter, "OnLayer"));
            var grid = Assert.IsType<Grid>(VisualTreeHelper.GetParent(onLayer));

            // The OnLayer is a background-only element: it must have no visual children, or the
            // label would inherit its opacity-0 off-state (the original defect).
            Assert.Equal(0, VisualTreeHelper.GetChildrenCount(onLayer));

            var onLayerIndex = -1;
            ContentPresenter? content = null;
            var contentIndex = -1;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(grid); i++)
            {
                var child = VisualTreeHelper.GetChild(grid, i);
                if (ReferenceEquals(child, onLayer))
                {
                    onLayerIndex = i;
                }

                if (child is ContentPresenter candidate)
                {
                    content = candidate;
                    contentIndex = i;
                }
            }

            Assert.NotNull(content);
            Assert.Equal(Visibility.Visible, content!.Visibility);

            // The label is a sibling above the background layer.
            Assert.True(onLayerIndex >= 0);
            Assert.True(contentIndex > onLayerIndex);

            return EffectiveOpacity(content);
        });

        Assert.True(result > 0, "the label must be effectively visible");
    }

    private static ContentPresenter Materialize(object content)
    {
        var dictionary = Assert.IsType<ResourceDictionary>(Application.LoadComponent(new Uri(
            "/TouchPanel;component/Widgets/ToggleWidget/ToggleWidgetTemplates.xaml",
            UriKind.Relative)));
        var template = Assert.IsType<DataTemplate>(dictionary["Widget.Toggle"]);

        var presenter = new ContentPresenter
        {
            Content = content,
            ContentTemplate = template,
        };

        // Materializing the template attaches the bindings and applies the triggers, including the
        // toggle button's control template that contains the OnLayer.
        presenter.Measure(new Size(120, 60));
        presenter.Arrange(new Rect(0, 0, 120, 60));
        presenter.UpdateLayout();
        return presenter;
    }

    private static double EffectiveOpacity(DependencyObject element)
    {
        var opacity = 1.0;
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is UIElement ui)
            {
                opacity *= ui.Opacity;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return opacity;
    }

    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        if (root is FrameworkElement element && string.Equals(element.Name, name, StringComparison.Ordinal))
        {
            return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindByName(VisualTreeHelper.GetChild(root, i), name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
