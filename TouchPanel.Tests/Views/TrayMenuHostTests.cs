using System.Windows;
using System.Windows.Controls;
using TouchPanel.Services.Tray;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Views;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Verifies the WPF mapping of the tray menu model (RF-005): headers, checked/disabled state,
/// and command dispatch on click. The menu is built but never opened, so no desktop UI is
/// touched; the native menu behavior is verified manually in T-14/MV-07.
/// </summary>
public sealed class TrayMenuHostTests
{
    [Fact]
    public async Task BuildMenu_MapsTheModelToMenuItemsWithTheirState()
    {
        using var sta = new StaThread();
        var items = new List<TrayMenuItem>
        {
            new(TrayMenuCommand.ShowHidePanel, "Show/Hide Panel"),
            new(TrayMenuCommand.OpenConfigurationFolder, "Open Configuration Folder", IsEnabled: false),
            new(TrayMenuCommand.Exit, "Edit Mode", IsChecked: true, IsCheckable: true),
        };

        await sta.InvokeAsync(() =>
        {
            var menu = TrayMenuHost.BuildMenu(items, _ => { });

            Assert.Equal(3, menu.Items.Count);

            var first = Assert.IsType<MenuItem>(menu.Items[0]);
            var second = Assert.IsType<MenuItem>(menu.Items[1]);
            var third = Assert.IsType<MenuItem>(menu.Items[2]);

            Assert.Equal("Show/Hide Panel", first.Header);
            Assert.True(first.IsEnabled);
            Assert.False(first.IsCheckable);
            Assert.False(first.IsChecked);

            Assert.Equal("Open Configuration Folder", second.Header);
            Assert.False(second.IsEnabled);

            Assert.Equal("Edit Mode", third.Header);
            Assert.True(third.IsEnabled);
            Assert.True(third.IsCheckable);
            Assert.True(third.IsChecked);
        });
    }

    [Fact]
    public async Task BuildMenu_WhenAnItemIsClicked_DispatchesItsCommand()
    {
        using var sta = new StaThread();
        var dispatched = new List<TrayMenuCommand>();
        var items = new List<TrayMenuItem>
        {
            new(TrayMenuCommand.ShowHidePanel, "Show/Hide Panel"),
            new(TrayMenuCommand.Exit, "Exit"),
        };

        await sta.InvokeAsync(() =>
        {
            var menu = TrayMenuHost.BuildMenu(items, dispatched.Add);
            var showHide = Assert.IsType<MenuItem>(menu.Items[0]);
            var exit = Assert.IsType<MenuItem>(menu.Items[1]);

            showHide.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, showHide));
            exit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, exit));
        });

        Assert.Equal(new[] { TrayMenuCommand.ShowHidePanel, TrayMenuCommand.Exit }, dispatched);
    }

    [Fact]
    public async Task BuildMenu_MapsTheAboutItemToAnEnabledMenuItem()
    {
        using var sta = new StaThread();
        var items = new List<TrayMenuItem>
        {
            new(TrayMenuCommand.About, "About TouchPanel…"),
        };

        await sta.InvokeAsync(() =>
        {
            var menu = TrayMenuHost.BuildMenu(items, _ => { });
            var about = Assert.IsType<MenuItem>(Assert.Single(menu.Items));

            Assert.Equal("About TouchPanel…", about.Header);
            Assert.True(about.IsEnabled);
            Assert.False(about.IsCheckable);
        });
    }

    [Fact]
    public async Task BuildMenu_WithAnEmptyModel_ProducesAnEmptyMenu()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var menu = TrayMenuHost.BuildMenu(Array.Empty<TrayMenuItem>(), _ => { });

            Assert.Empty(menu.Items);
        });
    }
}
