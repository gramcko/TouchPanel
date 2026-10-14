using System.Windows;
using System.Windows.Controls;
using TouchPanel.ViewModels;

namespace TouchPanel.Widgets;

/// <summary>
/// Selects the <c>DataTemplate</c> for one widget view model by its
/// <see cref="WidgetViewModelBase.TemplateKey"/> (DD-6): the host declares the selector once and
/// stays free of per-widget-instance logic. The template resource is looked up from the element
/// tree (and therefore the application resources), so each widget type's XAML can live in its own
/// resource dictionary.
/// </summary>
public sealed class WidgetTemplateSelector : DataTemplateSelector
{
    /// <summary>
    /// Resolves the template for <paramref name="item"/>. A widget view model yields its
    /// type-specific template, falling back to the unsupported placeholder when that key is not
    /// present in the resources; any other item yields <see langword="null"/>.
    /// </summary>
    /// <param name="item">The item being displayed.</param>
    /// <param name="container">The container used to look up the template resource.</param>
    /// <returns>The template to apply, or <see langword="null"/> when none applies.</returns>
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not WidgetViewModelBase viewModel || container is not FrameworkElement element)
        {
            return null;
        }

        if (element.TryFindResource(viewModel.TemplateKey) is DataTemplate template)
        {
            return template;
        }

        return element.TryFindResource(WidgetTemplateKeys.Unsupported) as DataTemplate;
    }
}
