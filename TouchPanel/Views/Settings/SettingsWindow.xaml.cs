using System.Windows;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Views.Settings;

/// <summary>
/// Tray-accessible settings window (T-17, RF-092/M2-RF-921). The code-behind contains only window
/// lifecycle glue — binding the view models supplied by the composition root, refreshing the monitor
/// picker when the window is first shown, and closing on the Close button. All behavior lives in
/// <see cref="ViewModels.Settings.SettingsViewModel"/>,
/// <see cref="ViewModels.Settings.MonitorPickerViewModel"/>,
/// <see cref="ViewModels.Settings.PagesEditorViewModel"/>, and
/// <see cref="ViewModels.Settings.AppearanceEditorViewModel"/>. Unlike the panel, this window is an
/// ordinary activating window because it is a deliberate user action (M2-RF-924).
/// </summary>
public partial class SettingsWindow : Window
{
    private MonitorPickerViewModel? _picker;
    private PagesEditorViewModel? _pages;

    /// <summary>Initializes a new instance of the <see cref="SettingsWindow"/> class (XAML/designer path).</summary>
    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Activated += OnActivated;
    }

    private void OnActivated(object? sender, EventArgs e) => _pages?.Refresh();

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class with the composition
    /// root's view models.
    /// </summary>
    /// <param name="settings">The settings presentation state; must not be <see langword="null"/>.</param>
    /// <param name="picker">The monitor-picker presentation state, or <see langword="null"/> when it is unavailable.</param>
    /// <param name="pages">The pages-management presentation state, or <see langword="null"/> when pages management is unavailable.</param>
    /// <param name="appearance">The appearance (theme / icon pack / accent) presentation state, or <see langword="null"/> when appearance editing is unavailable.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is <see langword="null"/>.</exception>
    public SettingsWindow(
        SettingsViewModel settings,
        MonitorPickerViewModel? picker = null,
        PagesEditorViewModel? pages = null,
        AppearanceEditorViewModel? appearance = null)
        : this()
    {
        ArgumentNullException.ThrowIfNull(settings);

        DataContext = settings;
        if (picker is not null)
        {
            _picker = picker;
            MonitorPicker.DataContext = picker;
        }

        if (pages is not null)
        {
            _pages = pages;
            PagesEditor.DataContext = pages;
        }
        else
        {
            // Pages management needs the page-switch coordinator; keep the section out of the
            // settings surface rather than showing inert controls.
            PagesEditor.Visibility = Visibility.Collapsed;
        }

        if (appearance is not null)
        {
            AppearanceEditor.DataContext = appearance;
        }
        else
        {
            // Appearance editing needs the shared settings coordinator; keep the section out of the
            // settings surface rather than showing inert controls.
            AppearanceEditor.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Reflect external hot reload/import changes and the current edit-mode gate whenever this
        // deliberate activating window is brought forward.
        _pages?.Refresh();

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
