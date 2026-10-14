using System.Windows.Controls;

namespace TouchPanel.Views.MonitorPicker;

/// <summary>
/// Monitor-picker view (T-17, RF-093). Pure XAML with no per-instance logic; the composition root
/// (through the settings window) assigns a <c>MonitorPickerViewModel</c> as its data context once
/// the configuration has loaded.
/// </summary>
public partial class MonitorPickerView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="MonitorPickerView"/> class.</summary>
    public MonitorPickerView()
    {
        InitializeComponent();
    }
}
