using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Theme;
using TouchPanel.ViewModels.Settings;
using TouchPanel.Views.MonitorPicker;
using TouchPanel.Views.Settings;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Settings-window wiring (T-17, RF-092/M2-RF-921..924): the window is an ordinary activating window
/// (deliberate user action), exposes the M2 affordances and the appearance editor (M3-RF-850) at the
/// touch minimum, and embeds the monitor picker bound to its view model. The XAML is instantiated on
/// an STA thread but never shown, so no desktop state is touched; the real live behavior is verified
/// manually (MV-M2-07).
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

    [Fact]
    public async Task SettingsWindow_ExposesTheTouchAppearanceSection()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var appearance = CreateAppearance();
            var window = CreateWindow(out _, appearance);
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                var section = Assert.IsType<StackPanel>(window.FindName("AppearanceEditor"));
                var theme = Assert.IsType<ComboBox>(window.FindName("ThemeSelector"));
                var pack = Assert.IsType<ComboBox>(window.FindName("IconPackSelector"));
                var transition = Assert.IsType<ComboBox>(window.FindName("PageTransitionSelector"));
                var accent = Assert.IsType<TextBox>(window.FindName("AccentTextBox"));
                var setAccent = Assert.IsType<Button>(window.FindName("SetAccentButton"));
                var clearAccent = Assert.IsType<Button>(window.FindName("ClearAccentButton"));

                return new
                {
                    SectionVisible = section.Visibility == Visibility.Visible,
                    BoundToViewModel = ReferenceEquals(section.DataContext, appearance),
                    ThemeCount = theme.Items.Count,
                    PackCount = pack.Items.Count,
                    TransitionCount = transition.Items.Count,
                    TransitionMinHeight = transition.MinHeight,
                    SelectedTransition = transition.SelectedItem as string,
                    SelectedTheme = theme.SelectedItem as string,
                    AccentHeight = accent.MinHeight,
                    SetAccentHeight = setAccent.MinHeight,
                    ClearAccentHeight = clearAccent.MinHeight,
                };
            }
            finally
            {
                window.Close();
            }
        });

        // M3-RF-850: the appearance editor now lives on the settings surface.
        Assert.True(result.SectionVisible);
        Assert.True(result.BoundToViewModel);
        Assert.Equal(ThemeIds.All.Count, result.ThemeCount);
        Assert.Equal(IconPackIds.All.Count, result.PackCount);
        Assert.Equal(PageTransitionModes.All.Count, result.TransitionCount);
        Assert.True(result.TransitionMinHeight >= 40);
        Assert.Equal(PageTransitionModes.Default, result.SelectedTransition);
        Assert.Equal("dark", result.SelectedTheme);

        // M3-RF-850/RF-025: the appearance affordances meet the 40 DIP touch minimum.
        Assert.True(result.AccentHeight >= 40);
        Assert.True(result.SetAccentHeight >= 40);
        Assert.True(result.ClearAccentHeight >= 40);
    }

    private static SettingsWindow CreateWindow(
        out MonitorPickerView pickerView,
        AppearanceEditorViewModel? appearance = null)
    {
        var config = DefaultConfigGenerator.Create();
        var picker = CreatePicker(out _);
        var settings = new SettingsViewModel(
            new FakeSettingsService(),
            () => config,
            importExport: null,
            new FakeConfigFilePicker(),
            "panel.json");

        var window = new SettingsWindow(settings, picker, appearance: appearance);
        pickerView = Assert.IsType<MonitorPickerView>(window.FindName("MonitorPicker"));
        return window;
    }

    private static AppearanceEditorViewModel CreateAppearance()
    {
        var config = DefaultConfigGenerator.Create();
        return new AppearanceEditorViewModel(new FakeSettingsService(), () => config.Appearance);
    }

    private static MonitorPickerViewModel CreatePicker(out FakeMonitorEnumerator enumerator)
    {
        var config = DefaultConfigGenerator.Create();
        enumerator = new FakeMonitorEnumerator();
        return new MonitorPickerViewModel(enumerator, new FakeSettingsService(), () => config.Monitor);
    }
}
