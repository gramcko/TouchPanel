using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.ViewModels;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Inline, expandable colour picker for the appearance fields (user-requested replacement for plain
/// hex entry). The collapsed 40 DIP header shows the current swatch and value (or "Inherited"/
/// "None"); a tap expands a six-column grid of 40 DIP swatches plus an exact-hex field. Selecting a
/// swatch applies the exact colour and collapses, so one tap is one undoable edit; the hex field
/// commits on Enter or focus loss, which keeps typing from creating per-keystroke undo noise.
/// </summary>
/// <remarks>
/// The control owns no catalogue semantics: <see cref="SelectedHex"/> is two-way bound to the
/// editor's existing colour property (which validates and commits), and the optional
/// <see cref="CommitCommand"/> lets a surface that applies explicitly (the pages editor) run its
/// apply pipeline after a selection. No <see cref="Popup"/> is used, so the property panel's touch
/// pan remains the single scroll mechanism.
/// </remarks>
public partial class InlineColorPicker : UserControl
{
    /// <summary>Message shown when the exact-hex input cannot be parsed.</summary>
    public const string InvalidColorMessage = "Color must be #RGB, #RRGGBB, or #AARRGGBB.";

    private static readonly IReadOnlyList<ColorSwatchOption> PaletteTiles = ColorSwatchOption.CreateTiles(allowNone: false);

    private bool _syncingHex;

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
        new PropertyMetadata(false, OnAllowNoneChanged));

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

    /// <summary>Identifies the <see cref="IsExpanded"/> dependency property.</summary>
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded),
        typeof(bool),
        typeof(InlineColorPicker),
        new PropertyMetadata(false, OnExpandedChanged));

    /// <summary>Identifies the <see cref="CommitCommand"/> dependency property.</summary>
    public static readonly DependencyProperty CommitCommandProperty = DependencyProperty.Register(
        nameof(CommitCommand),
        typeof(ICommand),
        typeof(InlineColorPicker),
        new PropertyMetadata(null));

    /// <summary>Initializes a new instance of the <see cref="InlineColorPicker"/> class.</summary>
    public InlineColorPicker()
    {
        InitializeComponent();
        RefreshTiles();
        DataObject.AddPastingHandler(HexBox, OnHexPasting);
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

    /// <summary>Gets or sets whether the optional "None" tile is offered (foreground/background/accent/on-icon/page).</summary>
    public bool AllowNone
    {
        get => (bool)GetValue(AllowNoneProperty);
        set => SetValue(AllowNoneProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the field is inherited/unset. The header then shows
    /// <see cref="NoneText"/> and the exact-hex field starts empty; the source property usually binds
    /// this one-way from the editor.
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

    /// <summary>Gets or sets whether the swatch grid and exact-hex field are expanded. Starts collapsed.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets or sets an optional command run after a selection or hex commit. The pages editor uses
    /// it to apply its background draft immediately; the widget editor leaves it unset because its
    /// two-way binding already commits through the undoable appearance path.
    /// </summary>
    public ICommand? CommitCommand
    {
        get => (ICommand?)GetValue(CommitCommandProperty);
        set => SetValue(CommitCommandProperty, value);
    }

    /// <summary>Gets the built-in palette tiles, in display order (six per row, no "None").</summary>
    public IReadOnlyList<ColorSwatchOption> Swatches => PaletteTiles;

    /// <summary>
    /// Validates and commits the current exact-hex text. Enter, a focus loss, and tests all use this
    /// path; invalid text is rejected with inline feedback and leaves the selection untouched.
    /// </summary>
    /// <returns><see langword="true"/> when a value was committed.</returns>
    public bool CommitHex()
    {
        var text = HexBox.Text.Trim();
        if (text.Length == 0)
        {
            if (!AllowNone)
            {
                ShowHexError(InvalidColorMessage);
                return false;
            }

            CommitSelection(string.Empty);
            return true;
        }

        if (!ColorPalette.TryParseHex(text, out var normalized) || normalized is null)
        {
            ShowHexError(InvalidColorMessage);
            return false;
        }

        CommitSelection(normalized);
        return true;
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((InlineColorPicker)d).UpdateVisual();

    private static void OnAllowNoneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (InlineColorPicker)d;
        picker.RefreshTiles();
        picker.UpdateVisual();
    }

    private static void OnExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (InlineColorPicker)d;
        if (picker.IsExpanded)
        {
            picker.SyncHexText();
            picker.ClearHexError();
        }
        else
        {
            picker.ClearHexError();
        }
    }

    private void RefreshTiles() => SwatchTiles.ItemsSource = ColorSwatchOption.CreateTiles(AllowNone);

    private void UpdateVisual()
    {
        var inherited = IsInherited || string.IsNullOrWhiteSpace(SelectedHex);
        HeaderLabel.Text = inherited ? NoneText : SelectedHex;
        CurrentSwatch.Background = inherited
            ? Brushes.Transparent
            : AppearanceBrushFactory.Create(SelectedHex) ?? Brushes.Transparent;

        if (IsExpanded && !HexBox.IsKeyboardFocusWithin)
        {
            SyncHexText();
        }
    }

    private void SyncHexText()
    {
        _syncingHex = true;
        HexBox.Text = IsInherited || string.IsNullOrWhiteSpace(SelectedHex) ? string.Empty : SelectedHex;
        _syncingHex = false;
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e) => IsExpanded = !IsExpanded;

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ColorSwatchOption option })
        {
            CommitSelection(option.Hex);
        }

        e.Handled = true;
    }

    private void CommitSelection(string hex)
    {
        IsInherited = hex.Length == 0;
        SelectedHex = hex;
        IsExpanded = false;
        ClearHexError();

        var command = CommitCommand;
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    private void OnHexPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_syncingHex)
        {
            return;
        }

        var candidate = HexBox.Text.Remove(HexBox.SelectionStart, HexBox.SelectionLength)
            .Insert(HexBox.SelectionStart, e.Text);
        e.Handled = !ColorPalette.IsAllowedHexInput(candidate);
    }

    private void OnHexPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            IsExpanded = false;
            e.Handled = true;
        }
    }

    private void OnHexLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!IsExpanded)
        {
            return;
        }

        CommitHex();
    }

    private void OnHexPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText)
            || e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string pasted)
        {
            e.CancelCommand();
            return;
        }

        var candidate = HexBox.Text.Remove(HexBox.SelectionStart, HexBox.SelectionLength)
            .Insert(HexBox.SelectionStart, pasted);
        if (!ColorPalette.IsAllowedHexInput(candidate))
        {
            e.CancelCommand();
        }
    }

    private void ShowHexError(string message)
    {
        HexErrorText.Text = message;
        HexErrorText.Visibility = Visibility.Visible;
    }

    private void ClearHexError()
    {
        HexErrorText.Text = string.Empty;
        HexErrorText.Visibility = Visibility.Collapsed;
    }
}
