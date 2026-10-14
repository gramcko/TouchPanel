using System.Windows;
using TouchPanel.ViewModels.Hotkeys;

namespace TouchPanel.Views.Hotkeys;

/// <summary>
/// Tray-accessible, touch-first hotkey editor (RF-061/M3-RF-611..619). The code-behind contains only
/// window lifecycle glue: binding the view model supplied by the composition root and closing on the
/// Close button. All behavior lives in <see cref="HotkeysEditorViewModel"/>. Unlike the panel, this
/// window is an ordinary activating window because it is a deliberate user action (the D-6 analogue,
/// M3-RF-616); opening/closing it never changes the panel's no-activate behavior.
/// </summary>
public partial class HotkeysEditorWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="HotkeysEditorWindow"/> class (XAML/designer path).</summary>
    public HotkeysEditorWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeysEditorWindow"/> class with the composition
    /// root's view model.
    /// </summary>
    /// <param name="viewModel">The hotkey-editor presentation state; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is <see langword="null"/>.</exception>
    public HotkeysEditorWindow(HotkeysEditorViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
