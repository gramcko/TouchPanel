using System.Windows.Controls;

namespace TouchPanel.Views;

/// <summary>
/// In-panel page selector (T-16, RF-091): a touch tab strip that drives a
/// <c>PageSwitcherViewModel</c>'s <c>SelectPageCommand</c>. The view is pure XAML with no
/// per-instance logic; the composition root assigns the view model as its data context once the
/// configuration has loaded.
/// </summary>
public partial class PageSwitcherView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="PageSwitcherView"/> class.</summary>
    public PageSwitcherView()
    {
        InitializeComponent();
    }
}
