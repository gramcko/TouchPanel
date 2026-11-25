using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.ViewModels;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Touch-sized colour field button (user-requested docked flyout replacement for inline expansion).
/// The header shows the current swatch and value (or "Inherited"/"None"); tapping it executes
/// <see cref="FieldCommand"/>, which opens the shared colour flyout. The swatch grid, the optional
/// "None" entry, and the exact-hex input live in the flyout, never in the property panel flow.
/// </summary>
public partial class InlineColorPicker : UserControl
{
    /// <summary>Identifies the <see cref="SelectedHex"/> dependency property (two-way by default).</summary>
    public static readonly DependencyProperty SelectedHexProperty = DependencyProperty.Register(
        nameof(SelectedHex),
        typeof(string),
        typeof(InlineColorPicker),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="AllowNone"/> dependency property.</summary>
    public static readonly DependencyProperty AllowNoneProperty = DependencyProperty.Register(
        nameof(AllowNone),
        typeof(bool),
        typeof(InlineColorPicker),
        new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="IsInherited"/> dependency property.</summary>
    public static readonly DependencyProperty IsInheritedProperty = DependencyProperty.Register(
        nameof(IsInherited),
        typeof(bool),
        typeof(InlineColorPicker),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="NoneText"/> dependency property.</summary>
    public static readonly DependencyProperty NoneTextProperty = DependencyProperty.Register(
        nameof(NoneText),
        typeof(string),
        typeof(InlineColorPicker),
        new PropertyMetadata("Inherited", OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="FieldCommand"/> dependency property.</summary>
    public static readonly DependencyProperty FieldCommandProperty = DependencyProperty.Register(
        nameof(FieldCommand),
        typeof(ICommand),
        typeof(InlineColorPicker),
        new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="FieldCommandParameter"/> dependency property.</summary>
    public static readonly DependencyProperty FieldCommandParameterProperty = DependencyProperty.Register(
        nameof(FieldCommandParameter),
        typeof(object),
        typeof(InlineColorPicker),
        new PropertyMetadata(null));

    /// <summary>Initializes a new instance of the <see cref="InlineColorPicker"/> class.</summary>
    public InlineColorPicker()
    {
        InitializeComponent();
        UpdateVisual();
    }

    /// <summary>
    /// Gets or sets the current colour as normalized hex text, or an empty string when the field is
    /// unset. Two-way bound to the editor property, which validates and commits the change.
    /// </summary>
    public string SelectedHex
    {
        get => (string)GetValue(SelectedHexProperty);
        set => SetCurrentValue(SelectedHexProperty, value ?? string.Empty);
    }

    /// <summary>Gets or sets whether the optional "None" entry is offered by the flyout.</summary>
    public bool AllowNone
    {
        get => (bool)GetValue(AllowNoneProperty);
        set => SetValue(AllowNoneProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the field is inherited/unset. The header then shows
    /// <see cref="NoneText"/>; the source property usually binds this one-way from the editor.
    /// </summary>
    public bool IsInherited
    {
        get => (bool)GetValue(IsInheritedProperty);
        set => SetCurrentValue(IsInheritedProperty, value);
    }

    /// <summary>Gets or sets the header text shown when the field is unset ("Inherited" or "None").</summary>
    public string NoneText
    {
        get => (string)GetValue(NoneTextProperty);
        set => SetValue(NoneTextProperty, value ?? string.Empty);
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

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((InlineColorPicker)d).UpdateVisual();

    private void UpdateVisual()
    {
        var inherited = IsInherited || string.IsNullOrWhiteSpace(SelectedHex);
        HeaderLabel.Text = inherited ? NoneText : SelectedHex;
        CurrentSwatch.Background = inherited
            ? Brushes.Transparent
            : AppearanceBrushFactory.Create(SelectedHex) ?? Brushes.Transparent;
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
