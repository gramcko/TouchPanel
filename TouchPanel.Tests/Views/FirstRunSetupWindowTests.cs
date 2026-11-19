using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Setup;
using TouchPanel.Views.Controls;
using TouchPanel.Views.Setup;

namespace TouchPanel.Tests.Views;

/// <summary>
/// BAML/wiring checks for the first-run setup window: it materializes on an STA thread, activates
/// (deliberate user action), exposes its touch-sized affordances, binds the monitor rows, and binds
/// the start-with-Windows checkbox. The window is never shown, so no desktop state is touched.
/// </summary>
public sealed class FirstRunSetupWindowTests
{
    [Fact]
    public async Task FirstRunSetupWindow_MaterializesAndBindsTheMonitorAndStartupChoices()
    {
        using var sta = new StaThread();

        var viewModel = await CreateViewModelAsync(withMonitors: true);
        viewModel.StartWithWindows = true;

        var snapshot = await sta.InvokeAsync(() =>
        {
            var window = new FirstRunSetupWindow(viewModel);
            try
            {
                WindowIconAssert.IsPackagedAppIcon(window);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                var list = Assert.IsType<ListBox>(window.FindName("MonitorList"));
                var startWithWindows = Assert.IsType<CheckBox>(window.FindName("StartWithWindowsCheckBox"));
                var save = Assert.IsType<Button>(window.FindName("SaveButton"));
                var skip = Assert.IsType<Button>(window.FindName("SkipButton"));
                var emptyText = Assert.IsType<TextBlock>(window.FindName("NoMonitorsText"));

                return new
                {
                    ShowActivated = window.ShowActivated,
                    MonitorCount = list.Items.Count,
                    StartWithWindowsChecked = startWithWindows.IsChecked,
                    StartWithWindowsMinHeight = startWithWindows.MinHeight,
                    SaveMinHeight = save.MinHeight,
                    SkipMinHeight = skip.MinHeight,
                    EmptyTextVisibility = emptyText.Visibility,
                    // Touch panning (user-reported fix, applied for consistency): the monitor list uses
                    // the custom touch behavior, so a vertical drag that starts on a row scrolls it.
                    MonitorListTouchPan = TouchPan.GetIsEnabled(list),
                    MonitorListPanningMode = ScrollViewer.GetPanningMode(list),
                    MonitorListHorizontalVisibility = ScrollViewer.GetHorizontalScrollBarVisibility(list),
                };
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(snapshot.ShowActivated);
        Assert.Equal(2, snapshot.MonitorCount);
        Assert.True(snapshot.StartWithWindowsChecked);
        Assert.True(snapshot.StartWithWindowsMinHeight >= 40);
        Assert.True(snapshot.SaveMinHeight >= 40);
        Assert.True(snapshot.SkipMinHeight >= 40);
        Assert.Equal(Visibility.Collapsed, snapshot.EmptyTextVisibility);
        Assert.True(snapshot.MonitorListTouchPan);
        Assert.Equal(PanningMode.None, snapshot.MonitorListPanningMode);
        Assert.Equal(ScrollBarVisibility.Disabled, snapshot.MonitorListHorizontalVisibility);
    }

    [Fact]
    public async Task FirstRunSetupWindow_WithNoMonitors_ShowsTheGracefulEmptyState()
    {
        using var sta = new StaThread();

        var viewModel = await CreateViewModelAsync(withMonitors: false);

        var snapshot = await sta.InvokeAsync(() =>
        {
            var window = new FirstRunSetupWindow(viewModel);
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                var list = Assert.IsType<ListBox>(window.FindName("MonitorList"));
                var emptyText = Assert.IsType<TextBlock>(window.FindName("NoMonitorsText"));
                var startWithWindows = Assert.IsType<CheckBox>(window.FindName("StartWithWindowsCheckBox"));

                return new
                {
                    MonitorCount = list.Items.Count,
                    EmptyTextVisibility = emptyText.Visibility,
                    StartWithWindowsAvailable = startWithWindows.IsEnabled,
                };
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(0, snapshot.MonitorCount);
        Assert.Equal(Visibility.Visible, snapshot.EmptyTextVisibility);
        Assert.True(snapshot.StartWithWindowsAvailable);
    }

    private static async Task<FirstRunSetupViewModel> CreateViewModelAsync(bool withMonitors)
    {
        var enumerator = new FakeMonitorEnumerator();
        if (withMonitors)
        {
            enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(friendlyName: "Left", x: 0));
            enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(
                friendlyName: "Touch",
                x: 1920,
                primary: true,
                gdiName: @"\\.\DISPLAY2"));
        }

        var viewModel = new FirstRunSetupViewModel(enumerator, new FakeSettingsService());
        await viewModel.InitializeAsync();
        return viewModel;
    }
}
