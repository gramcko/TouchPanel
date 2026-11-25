using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TouchPanel.Config;
using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Docked icon/colour picker flyout shown to the left of the property panel (user-requested
/// replacement for inline picker expansion). The control is presentation only: the owning
/// <see cref="PropertyPickerFlyoutViewModel"/> owns the target, the MRU palette, and the apply
/// semantics. The exact-hex field commits on Enter or focus loss so typing never creates per-keystroke
/// undo steps.
/// </summary>
public partial class PropertyFlyout : UserControl
{
    private PropertyPickerFlyoutViewModel? _viewModel;
    private bool _syncingHex;

    /// <summary>Initializes a new instance of the <see cref="PropertyFlyout"/> class.</summary>
    public PropertyFlyout()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(FlyoutHexBox, OnHexPasting);
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Validates and applies the current exact-hex text through the view model. Enter, focus loss,
    /// and tests use this path; invalid text is rejected with inline feedback.
    /// </summary>
    /// <returns><see langword="true"/> when a value was applied.</returns>
    public bool CommitHex()
    {
        if (_viewModel is null)
        {
            return false;
        }

        var committed = _viewModel.TryCommitHex(FlyoutHexBox.Text);
        if (!committed)
        {
            return false;
        }

        SyncHexText();
        return true;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as PropertyPickerFlyoutViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        SyncHexText();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PropertyPickerFlyoutViewModel.IsColorFlyout)
            or nameof(PropertyPickerFlyoutViewModel.SelectedHex)
            or nameof(PropertyPickerFlyoutViewModel.IsInherited))
        {
            SyncHexText();
        }
    }

    private void SyncHexText()
    {
        if (FlyoutHexBox.IsKeyboardFocusWithin)
        {
            return;
        }

        _syncingHex = true;
        FlyoutHexBox.Text = _viewModel is null || _viewModel.IsInherited
            ? string.Empty
            : _viewModel.SelectedHex;
        _syncingHex = false;
    }

    private void OnHexPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_syncingHex)
        {
            return;
        }

        var candidate = FlyoutHexBox.Text.Remove(FlyoutHexBox.SelectionStart, FlyoutHexBox.SelectionLength)
            .Insert(FlyoutHexBox.SelectionStart, e.Text);
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
            _viewModel?.Close();
            e.Handled = true;
        }
    }

    private void OnHexLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_viewModel?.IsColorFlyout == true)
        {
            CommitHex();
        }
    }

    private void OnHexPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText)
            || e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string pasted)
        {
            e.CancelCommand();
            return;
        }

        var candidate = FlyoutHexBox.Text.Remove(FlyoutHexBox.SelectionStart, FlyoutHexBox.SelectionLength)
            .Insert(FlyoutHexBox.SelectionStart, pasted);
        if (!ColorPalette.IsAllowedHexInput(candidate))
        {
            e.CancelCommand();
        }
    }
}
