using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TouchPanel.Config;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Touch-sized field button for the built-in icon catalogs (user-requested docked flyout). The
/// control only renders the current entry; tapping it executes <see cref="FieldCommand"/>, which the
/// property panel binds to its flyout toggle. No tiles are rendered here, so the property panel never
/// changes height when a picker opens.
/// </summary>
public partial class InlineIconPicker : UserControl
{
    /// <summary>Identifies the <see cref="ItemsSource"/> dependency property.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(InlineIconPicker),
        new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="SelectedItem"/> dependency property (two-way by default).</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(IconOption),
        typeof(InlineIconPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Identifies the <see cref="FieldCommand"/> dependency property.</summary>
    public static readonly DependencyProperty FieldCommandProperty = DependencyProperty.Register(
        nameof(FieldCommand),
        typeof(ICommand),
        typeof(InlineIconPicker),
        new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="FieldCommandParameter"/> dependency property.</summary>
    public static readonly DependencyProperty FieldCommandParameterProperty = DependencyProperty.Register(
        nameof(FieldCommandParameter),
        typeof(object),
        typeof(InlineIconPicker),
        new PropertyMetadata(null));

    /// <summary>Initializes a new instance of the <see cref="InlineIconPicker"/> class.</summary>
    public InlineIconPicker() => InitializeComponent();

    /// <summary>Gets or sets the entries the field labels (kept for binding compatibility).</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the current entry. Two-way bound to the property editor's selection so the field
    /// header reflects the applied icon.
    /// </summary>
    public IconOption? SelectedItem
    {
        get => (IconOption?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Gets or sets the command invoked when the field is tapped (opens/toggles the flyout).</summary>
    public ICommand? FieldCommand
    {
        get => (ICommand?)GetValue(FieldCommandProperty);
        set => SetValue(FieldCommandProperty, value);
    }

    /// <summary>Gets or sets the parameter passed to <see cref="FieldCommand"/>.</summary>
    public object? FieldCommandParameter
    {
        get => GetValue(FieldCommandParameterProperty);
        set => SetValue(FieldCommandParameterProperty, value);
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        var command = FieldCommand;
        if (command?.CanExecute(FieldCommandParameter) == true)
        {
            command.Execute(FieldCommandParameter);
        }

        e.Handled = true;
    }
}
