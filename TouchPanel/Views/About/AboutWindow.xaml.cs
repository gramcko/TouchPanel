using System.Windows;
using TouchPanel.ViewModels.About;

namespace TouchPanel.Views.About;

/// <summary>
/// About / version window. The code-behind contains only lifecycle glue (binding the view model
/// supplied by the composition root and closing); all behavior lives in
/// <see cref="AboutViewModel"/>. This is an ordinary activating window because opening it is a
/// deliberate user action and never changes the panel's no-activate state.
/// </summary>
public partial class AboutWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="AboutWindow"/> class (XAML/designer path).</summary>
    public AboutWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutWindow"/> class with the composition root's
    /// view model.
    /// </summary>
    /// <param name="viewModel">The About presentation state; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is <see langword="null"/>.</exception>
    public AboutWindow(AboutViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
