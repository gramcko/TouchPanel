using System.Windows;
using System.Windows.Controls;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Views;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Page-selector host wiring (T-16, RF-091/M2-RF-911): the control binds the view model's page list,
/// collapses when only one page is configured, and keeps every tab at the 40 DIP touch minimum. The
/// XAML is instantiated on an STA thread but never shown, so no desktop state is touched; the real
/// non-activating touch behavior is verified manually (MV-M2-07).
/// </summary>
public sealed class PageSwitcherViewTests
{
    [Fact]
    public async Task Selector_BindsThePagesAndIsVisibleOnlyWithMultiplePages()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var view = new PageSwitcherView();
            view.DataContext = new PageSwitcherViewModel(TwoPages(), new FakePageSwitchService());
            view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);

            var root = Assert.IsType<Grid>(view.FindName("SelectorRoot"));
            var items = Assert.IsType<ItemsControl>(view.FindName("PageItems"));

            var itemCount = items.Items.Count;
            var multipleVisible = root.Visibility;

            view.DataContext = new PageSwitcherViewModel(OnePage(), new FakePageSwitchService());
            view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);

            return (itemCount, multipleVisible, singleVisibility: root.Visibility);
        });

        Assert.Equal(2, result.itemCount);
        Assert.Equal(Visibility.Visible, result.multipleVisible);
        Assert.Equal(Visibility.Collapsed, result.singleVisibility);
    }

    [Fact]
    public async Task Selector_TabStyleMeetsTheTouchMinimumAndIsNotHoverOnly()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var view = new PageSwitcherView();
            var style = (Style)view.Resources["PageTabButtonStyle"];
            var minWidth = ReadDoubleSetter(style, FrameworkElement.MinWidthProperty);
            var minHeight = ReadDoubleSetter(style, FrameworkElement.MinHeightProperty);
            var focusable = ReadBoolSetter(style, UIElement.FocusableProperty);
            return (minWidth, minHeight, focusable);
        });

        Assert.True(result.minWidth >= 40, $"Page tabs must be at least 40 DIP wide (was {result.minWidth}).");
        Assert.True(result.minHeight >= 40, $"Page tabs must be at least 40 DIP tall (was {result.minHeight}).");
        Assert.False(result.focusable, "Page tabs must not require keyboard focus.");
    }

    private static double ReadDoubleSetter(Style style, DependencyProperty property)
    {
        foreach (var setterBase in style.Setters)
        {
            if (setterBase is Setter { Property: var candidate, Value: double value } && candidate == property)
            {
                return value;
            }
        }

        return 0;
    }

    private static bool ReadBoolSetter(Style style, DependencyProperty property)
    {
        foreach (var setterBase in style.Setters)
        {
            if (setterBase is Setter { Property: var candidate, Value: bool value } && candidate == property)
            {
                return value;
            }
        }

        return true;
    }

    private static PanelConfig OnePage() => TwoPages(pages: ["only"]);

    private static PanelConfig TwoPages(string[]? pages = null)
    {
        pages ??= ["first", "second"];
        return new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = pages[0],
            Pages = [.. pages.Select(id => new PageConfig { Id = id, Widgets = [] })],
        };
    }
}
