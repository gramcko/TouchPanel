using System.Windows;
using System.Windows.Controls;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Widgets;

/// <summary>
/// Template selection (T-10, DD-6): the selector resolves each widget view model's template key
/// from the element tree resources, falls back to the unsupported placeholder when the type
/// template is missing, and ignores non-widget items. Runs on a background STA thread because
/// template/resource objects are dispatcher-affine; nothing is shown or activated.
/// </summary>
public sealed class WidgetTemplateSelectorTests
{
    [Fact]
    public async Task SelectTemplate_KnownTemplateKey_ResolvesTheTypeTemplate()
    {
        using var sta = new StaThread();

        var resolved = await sta.InvokeAsync(() =>
        {
            var expected = new DataTemplate();
            var container = new ContentPresenter();
            container.Resources.Add(WidgetTemplateKeys.Button, expected);

            var selector = new WidgetTemplateSelector();
            var viewModel = new ButtonWidgetViewModel(new ButtonWidgetConfig { Id = "b" });

            return ReferenceEquals(expected, selector.SelectTemplate(viewModel, container));
        });

        Assert.True(resolved);
    }

    [Fact]
    public async Task SelectTemplate_MissingTypeTemplate_FallsBackToTheUnsupportedTemplate()
    {
        using var sta = new StaThread();

        var resolved = await sta.InvokeAsync(() =>
        {
            var fallback = new DataTemplate();
            var container = new ContentPresenter();
            container.Resources.Add(WidgetTemplateKeys.Unsupported, fallback);

            var selector = new WidgetTemplateSelector();
            var viewModel = new ButtonWidgetViewModel(new ButtonWidgetConfig { Id = "b" });

            return ReferenceEquals(fallback, selector.SelectTemplate(viewModel, container));
        });

        Assert.True(resolved);
    }

    [Fact]
    public async Task SelectTemplate_NonViewModelItem_ReturnsNull()
    {
        using var sta = new StaThread();

        var template = await sta.InvokeAsync(() =>
        {
            var container = new ContentPresenter();
            container.Resources.Add(WidgetTemplateKeys.Button, new DataTemplate());

            return new WidgetTemplateSelector().SelectTemplate("not a widget", container);
        });

        Assert.Null(template);
    }

    [Fact]
    public async Task SelectTemplate_ViewModelWithoutAnyTemplateResource_ReturnsNull()
    {
        using var sta = new StaThread();

        var template = await sta.InvokeAsync(() =>
        {
            var selector = new WidgetTemplateSelector();
            var viewModel = new ButtonWidgetViewModel(new ButtonWidgetConfig { Id = "b" });

            return selector.SelectTemplate(viewModel, new ContentPresenter());
        });

        Assert.Null(template);
    }
}
