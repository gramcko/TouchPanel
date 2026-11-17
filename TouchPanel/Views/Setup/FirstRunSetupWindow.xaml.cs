using System.ComponentModel;
using System.Windows;
using TouchPanel.ViewModels.Setup;

namespace TouchPanel.Views.Setup;

/// <summary>
/// First-run setup window. The code-behind contains only window lifecycle glue: it binds the
/// composition root's <see cref="FirstRunSetupViewModel"/>, saves on the Save button, records the
/// skip on Skip (or when the window is closed without saving), and closes. All behavior lives in
/// the view model. This is an ordinary activating window because it is a deliberate user action.
/// </summary>
public partial class FirstRunSetupWindow : Window
{
    private readonly FirstRunSetupViewModel? _viewModel;

    /// <summary>Initializes a new instance of the <see cref="FirstRunSetupWindow"/> class (XAML/designer path).</summary>
    public FirstRunSetupWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FirstRunSetupWindow"/> class with the composition
    /// root's view model.
    /// </summary>
    /// <param name="viewModel">The first-run presentation state; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is <see langword="null"/>.</exception>
    public FirstRunSetupWindow(FirstRunSetupViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        DataContext = viewModel;
        Closing += OnClosing;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is { } viewModel)
        {
            try
            {
                await viewModel.ApplyAsync();
            }
            catch (Exception)
            {
                // The view model already logs and contains failures; the window must still close so
                // startup can continue with the defaults.
            }
        }

        Close();
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.Skip();
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Closing with the window chrome is treated as Skip: nothing is saved and startup continues
        // with the generated defaults.
        if (_viewModel is { Applied: false, Skipped: false } viewModel)
        {
            viewModel.Skip();
        }
    }
}
