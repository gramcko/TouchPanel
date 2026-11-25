using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TouchPanel.Config;
using TouchPanel.ViewModels;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Shared colour flyout (user-requested picker): MRU-first swatches plus the exact-hex field. The
/// <see cref="ColorPickerFlyoutViewModel"/> owns the palette and apply semantics; the control only
/// filters/commits the hex text and keeps it in sync with the current selection.
/// </summary>
public partial class ColorPickerFlyout : UserControl
{
    private ColorPickerFlyoutViewModel? _viewModel;
    private bool _syncingHex;

    /// <summary>Initializes a new instance of the <see cref="ColorPickerFlyout"/> class.</summary>
    public ColorPickerFlyout()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(FlyoutHexBox, OnHexPasting);
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Validates and applies the current hex text through the view model.</summary>
    /// <returns><see langword="true"/> when a value was applied.</returns>
    public bool CommitHex()
    {
        if (_viewModel is null || !_viewModel.TryCommitHex(FlyoutHexBox.Text))
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

        _viewModel = e.NewValue as ColorPickerFlyoutViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        SyncHexText();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ColorPickerFlyoutViewModel.IsOpen)
            or nameof(ColorPickerFlyoutViewModel.SelectedHex)
            or nameof(ColorPickerFlyoutViewModel.IsInherited))
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
        if (_viewModel?.IsOpen == true)
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
