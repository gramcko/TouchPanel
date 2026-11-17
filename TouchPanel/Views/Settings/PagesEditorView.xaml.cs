using System.Windows.Controls;

namespace TouchPanel.Views.Settings;

/// <summary>
/// Touch pages management surface (user-requested): a pure XAML view over a
/// <see cref="ViewModels.Settings.PagesEditorViewModel"/>. All add/rename/remove/open behavior and
/// persistence live in the view model; this class has no per-instance logic.
/// </summary>
public partial class PagesEditorView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="PagesEditorView"/> class.</summary>
    public PagesEditorView()
    {
        InitializeComponent();
    }
}
