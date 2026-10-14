using System.Windows;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Views.Settings;

/// <summary>
/// Tray-accessible settings window (T-17, RF-092/M2-RF-921). The code-behind contains only window
/// lifecycle glue — binding the view models supplied by the composition root, refreshing the monitor
/// picker when the window is first shown, and closing on the Close button. All behavior lives in
/// <see cref="ViewModels.Settings.SettingsViewModel"/> and
/// <see cref="ViewModels.Settings.MonitorPickerViewModel"/>. Unlike the panel, this window is an
/// ordinary activating window because it is a deliberate user action (M2-RF-924).
/// </summary>
public partial class SettingsWindow : Window
{
    private MonitorPickerViewModel? _picker;

    /// <summary>Initializes a new instance of the <see cref="SettingsWindow"/> class (XAML/designer path).</summary>
    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class with the composition
    /// root's view models.
    /// </summary>
    /// <param name="settings">The settings presentation state; must not be <see langword="null"/>.</param>
    /// <param name="picker">The monitor-picker presentation state, or <see langword="null"/> when it is unavailable.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is <see langword="null"/>.</exception>
    public SettingsWindow(SettingsViewModel settings, MonitorPickerViewModel? picker = null)
        : this()
    {
        ArgumentNullException.ThrowIfNull(settings);

        DataContext = settings;
        if (picker is not null)
        {
            _picker = picker;
            MonitorPicker.DataContext = picker;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_picker is not { } picker)
        {
            return;
        }

        try
        {
            await picker.RefreshAsync();
        }
        catch (Exception)
        {
            // M2-RF-925: a discovery failure must never take the settings window down; the picker
            // already surfaces an explanatory status and stays usable.
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
