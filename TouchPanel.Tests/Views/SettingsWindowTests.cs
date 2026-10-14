using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Settings;
using TouchPanel.Views.MonitorPicker;
using TouchPanel.Views.Settings;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Settings-window wiring (T-17, RF-092/M2-RF-921..924): the window is an ordinary activating window
/// (deliberate user action), exposes the M2 affordances at the touch minimum, and embeds the monitor
/// picker bound to its view model. The XAML is instantiated on an STA thread but never shown, so no
/// desktop state is touched; the real live behavior is verified manually (MV-M2-07).
/// </summary>
public sealed class SettingsWindowTests
{
    [Fact]
    public async Task SettingsWindow_ExposesTheM2AffordancesAtTheTouchMinimum()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = CreateWindow(out var pickerView);
            try
            {
                // Force data binding so the enabled/bound state reflects the view model even though
                // the window is never shown.
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                var alwaysOnTop = Assert.IsType<CheckBox>(window.FindName("AlwaysOnTopCheckBox"));
                var startWithWindows = Assert.IsType<CheckBox>(window.FindName("StartWithWindowsCheckBox"));
                var slider = Assert.IsType<Slider>(window.FindName("PollIntervalSlider"));
                var applyInterval = Assert.IsType<Button>(window.FindName("ApplyIntervalButton"));
                var import = Assert.IsType<Button>(window.FindName("ImportButton"));
                var export = Assert.IsType<Button>(window.FindName("ExportButton"));
                var close = Assert.IsType<Button>(window.FindName("CloseButton"));

                return new
                {
                    ShowActivated = window.ShowActivated,
                    AlwaysOnTopMinHeight = alwaysOnTop.MinHeight,
                    StartWithWindowsMinHeight = startWithWindows.MinHeight,
                    SliderMinHeight = slider.MinHeight,
                    ApplyIntervalMinHeight = applyInterval.MinHeight,
                    ImportEnabled = import.IsEnabled,
                    ExportEnabled = export.IsEnabled,
                    CloseMinHeight = close.MinHeight,
                    PickerName = pickerView.Name,
                    HasMonitorItems = pickerView.FindName("MonitorItems") is ItemsControl,
                };
            }
            finally
            {
                window.Close();
            }
        });

        // M2-RF-924: a deliberate activating window.
        Assert.True(result.ShowActivated);

        // RF-025/D-9 spirit: every affordance meets the 40 DIP touch minimum.
        Assert.True(result.AlwaysOnTopMinHeight >= 40);
        Assert.True(result.StartWithWindowsMinHeight >= 40);
        Assert.True(result.SliderMinHeight >= 40);
        Assert.True(result.ApplyIntervalMinHeight >= 40);
        Assert.True(result.CloseMinHeight >= 40);

        // Import/export are unavailable until the file-backed configuration is live.
        Assert.False(result.ImportEnabled);
        Assert.False(result.ExportEnabled);

        Assert.Equal("MonitorPicker", result.PickerName);
        Assert.True(result.HasMonitorItems);
    }

    [Fact]
    public async Task MonitorPickerView_BindsTheMonitorRows()
    {
        using var sta = new StaThread();

        var picker = CreatePicker(out var enumerator);
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(friendlyName: "Left", primary: true));
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(friendlyName: "Right", x: 1920, gdiName: @"\\.\DISPLAY2"));

        await picker.RefreshAsync();

        var itemCount = await sta.InvokeAsync(() =>
        {
            var view = new MonitorPickerView { DataContext = picker };
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var items = Assert.IsType<ItemsControl>(view.FindName("MonitorItems"));
            return items.Items.Count;
        });

        Assert.Equal(2, itemCount);
    }

    private static SettingsWindow CreateWindow(out MonitorPickerView pickerView)
    {
        var config = DefaultConfigGenerator.Create();
        var picker = CreatePicker(out _);
        var settings = new SettingsViewModel(
            new FakeSettingsService(),
            () => config,
            importExport: null,
            new FakeConfigFilePicker(),
            "panel.json");

        var window = new SettingsWindow(settings, picker);
        pickerView = Assert.IsType<MonitorPickerView>(window.FindName("MonitorPicker"));
        return window;
    }

    private static MonitorPickerViewModel CreatePicker(out FakeMonitorEnumerator enumerator)
    {
        var config = DefaultConfigGenerator.Create();
        enumerator = new FakeMonitorEnumerator();
        return new MonitorPickerViewModel(enumerator, new FakeSettingsService(), () => config.Monitor);
    }
}
