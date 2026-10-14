using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Persistent disabled-indicator coverage (RF-047/RF-079): loads the real Button/Toggle template
/// dictionaries and materializes each template on an STA thread (like the Gauge template test),
/// proving that a disabled widget shows an always-visible badge instead of relying on the
/// hover-only tooltip, and that the full configured reason stays available through the tooltip.
/// No window or desktop state is touched.
/// </summary>
public sealed class WidgetDisabledIndicatorTemplateTests
{
    [Theory]
    [InlineData("Button", true)]
    [InlineData("Toggle", true)]
    [InlineData("Button", false)]
    [InlineData("Toggle", false)]
    public async Task WidgetTemplate_ReflectsThePersistentDisabledIndicator(string kind, bool disabled)
    {
        using var sta = new StaThread();
        const string reason = "The bound action is not valid.";

        var result = await sta.InvokeAsync(() =>
        {
            var (template, viewModel) = Create(kind, disabled, disabled ? reason : null);
            var presenter = Materialize(template, viewModel);

            var badge = FindByName(presenter, "DisabledBadge");
            var errorBadge = FindByName(presenter, "ErrorBadge");
            Assert.NotNull(badge);
            Assert.NotNull(errorBadge);

            return (Disabled: badge!.Visibility, Tooltip: badge.ToolTip as string, Error: errorBadge!.Visibility);
        });

        Assert.Equal(disabled ? Visibility.Visible : Visibility.Collapsed, result.Disabled);
        Assert.Equal(disabled ? reason : null, result.Tooltip);
        Assert.Equal(Visibility.Collapsed, result.Error);
    }

    [Fact]
    public void ShowsDisabledIndicator_TracksEnablementAndTheConfiguredReason()
    {
        var disabled = new ButtonWidgetViewModel(new ButtonWidgetConfig
        {
            Id = "b1",
            Width = 40,
            Height = 40,
            IsEnabled = false,
        });
        Assert.True(disabled.ShowsDisabledIndicator);

        var reasonOnly = new ButtonWidgetViewModel(new ButtonWidgetConfig
        {
            Id = "b2",
            Width = 40,
            Height = 40,
            DisabledReason = "kept for diagnosis",
        });
        Assert.True(reasonOnly.ShowsDisabledIndicator);

        var enabled = new ButtonWidgetViewModel(new ButtonWidgetConfig
        {
            Id = "b3",
            Width = 40,
            Height = 40,
        });
        Assert.False(enabled.ShowsDisabledIndicator);
    }

    private static (DataTemplate Template, WidgetViewModelBase ViewModel) Create(
        string kind,
        bool disabled,
        string? reason)
    {
        var isButton = string.Equals(kind, "Button", StringComparison.Ordinal);
        var dictionary = Assert.IsType<ResourceDictionary>(Application.LoadComponent(new Uri(
            isButton
                ? "/TouchPanel;component/Widgets/ButtonWidget/ButtonWidgetTemplates.xaml"
                : "/TouchPanel;component/Widgets/ToggleWidget/ToggleWidgetTemplates.xaml",
            UriKind.Relative)));
        var template = Assert.IsType<DataTemplate>(dictionary[isButton ? "Widget.Button" : "Widget.Toggle"]);

        WidgetViewModelBase viewModel = isButton
            ? new ButtonWidgetViewModel(new ButtonWidgetConfig
            {
                Id = "button-1",
                X = 0,
                Y = 0,
                Width = 120,
                Height = 60,
                Label = "Go",
                IsEnabled = !disabled,
                DisabledReason = reason,
            })
            : new ToggleWidgetViewModel(new ToggleWidgetConfig
            {
                Id = "toggle-1",
                X = 0,
                Y = 0,
                Width = 120,
                Height = 60,
                Label = "Toggle",
                IsEnabled = !disabled,
                DisabledReason = reason,
            });

        return (template, viewModel);
    }

    private static ContentPresenter Materialize(DataTemplate template, object content)
    {
        var presenter = new ContentPresenter
        {
            Content = content,
            ContentTemplate = template,
        };

        // Materializing the template attaches the bindings and applies the DataTriggers.
        presenter.Measure(new Size(120, 60));
        presenter.Arrange(new Rect(0, 0, 120, 60));
        presenter.UpdateLayout();
        return presenter;
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
