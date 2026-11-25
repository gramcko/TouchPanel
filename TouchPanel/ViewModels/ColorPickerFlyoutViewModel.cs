using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TouchPanel.Config;
using TouchPanel.Views.Controls;

namespace TouchPanel.ViewModels;

/// <summary>
/// Reusable colour-flyout state (user-requested colour picker): the MRU palette, the optional "None"
/// entry, and the exact-hex entry over delegate callbacks. The widget property panel has its own
/// icon/colour flyout view model; the pages editor uses this simpler colour-only one for the page
/// background field.
/// </summary>
public sealed class ColorPickerFlyoutViewModel : INotifyPropertyChanged
{
    /// <summary>Message shown when the exact-hex input cannot be parsed.</summary>
    public const string InvalidColorMessage = "Color must be #RGB, #RRGGBB, or #AARRGGBB.";

    private readonly IRecentColorPalette _recentColors;
    private readonly Func<string?> _readHex;
    private readonly Func<bool> _readInherited;
    private readonly Action<string> _apply;
    private readonly string _title;
    private readonly bool _allowNone;
    private IReadOnlyList<ColorSwatchOption> _colorTiles = [];
    private string _selectedHex = string.Empty;
    private bool _isInherited;
    private string _hexError = string.Empty;
    private bool _isOpen;

    /// <summary>Initializes a new instance of the <see cref="ColorPickerFlyoutViewModel"/> class.</summary>
    /// <param name="recentColors">The shared MRU palette.</param>
    /// <param name="readHex">Reads the current colour text.</param>
    /// <param name="readInherited">Reads whether the field is unset.</param>
    /// <param name="apply">Applies a normalized colour (blank clears).</param>
    /// <param name="title">The flyout heading.</param>
    /// <param name="allowNone">Whether the "None" tile is offered.</param>
    public ColorPickerFlyoutViewModel(
        IRecentColorPalette recentColors,
        Func<string?> readHex,
        Func<bool> readInherited,
        Action<string> apply,
        string title,
        bool allowNone = true)
    {
        ArgumentNullException.ThrowIfNull(recentColors);
        ArgumentNullException.ThrowIfNull(readHex);
        ArgumentNullException.ThrowIfNull(readInherited);
        ArgumentNullException.ThrowIfNull(apply);

        _recentColors = recentColors;
        _readHex = readHex;
        _readInherited = readInherited;
        _apply = apply;
        _title = title;
        _allowNone = allowNone;

        SelectColorCommand = new RelayCommand<ColorSwatchOption>(SelectColor);
        CloseCommand = new RelayCommand(Close);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the command that selects a tile and closes the flyout.</summary>
    public ICommand SelectColorCommand { get; }

    /// <summary>Gets the command that closes the flyout.</summary>
    public ICommand CloseCommand { get; }

    /// <summary>Gets the flyout heading.</summary>
    public string Title => _title;

    /// <summary>Gets a value indicating whether the flyout is open.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    /// <summary>Gets the colour tiles, MRU first.</summary>
    public IReadOnlyList<ColorSwatchOption> ColorTiles
    {
        get => _colorTiles;
        private set => SetField(ref _colorTiles, value);
    }

    /// <summary>Gets the current colour as normalized hex, or an empty string.</summary>
    public string SelectedHex
    {
        get => _selectedHex;
        private set => SetField(ref _selectedHex, value);
    }

    /// <summary>Gets a value indicating whether the current field is unset.</summary>
    public bool IsInherited
    {
        get => _isInherited;
        private set => SetField(ref _isInherited, value);
    }

    /// <summary>Gets a value indicating whether the "None" entry is offered.</summary>
    public bool AllowNone => _allowNone;

    /// <summary>Gets the latest hex validation message, or an empty string.</summary>
    public string HexError
    {
        get => _hexError;
        private set
        {
            if (SetField(ref _hexError, value))
            {
                OnPropertyChanged(nameof(HasHexError));
            }
        }
    }

    /// <summary>Gets a value indicating whether the hex error text is visible.</summary>
    public bool HasHexError => HexError.Length > 0;

    /// <summary>Toggles the flyout (a second tap closes it).</summary>
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        Open();
    }

    /// <summary>Opens the flyout and refreshes the MRU-ordered tiles from the shared palette.</summary>
    public void Open()
    {
        ColorTiles = ColorSwatchOption.CreateTiles(_recentColors.Colors, _allowNone);
        SelectedHex = _readHex() ?? string.Empty;
        IsInherited = _readInherited();
        HexError = string.Empty;
        IsOpen = true;
    }

    /// <summary>Closes the flyout without changing the value.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        HexError = string.Empty;
    }

    /// <summary>Re-reads the value and MRU palette (used after an external change).</summary>
    public void Refresh()
    {
        ColorTiles = ColorSwatchOption.CreateTiles(_recentColors.Colors, _allowNone);
        SelectedHex = _readHex() ?? string.Empty;
        IsInherited = _readInherited();
    }

    /// <summary>
    /// Applies the exact-hex text (or clears when blank and "None" is allowed) and closes on success.
    /// </summary>
    /// <param name="text">The typed value.</param>
    /// <returns><see langword="true"/> when a value was applied.</returns>
    public bool TryCommitHex(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            if (!_allowNone)
            {
                HexError = InvalidColorMessage;
                return false;
            }

            _apply(string.Empty);
            SelectedHex = string.Empty;
            IsInherited = true;
            Close();
            return true;
        }

        if (!ColorPalette.TryParseHex(value, out var normalized) || normalized is null)
        {
            HexError = InvalidColorMessage;
            return false;
        }

        _apply(normalized);
        SelectedHex = normalized;
        IsInherited = false;
        _recentColors.Record(normalized);
        Close();
        return true;
    }

    private void SelectColor(ColorSwatchOption? option)
    {
        if (option is null)
        {
            return;
        }

        if (option.IsNone)
        {
            _apply(string.Empty);
            SelectedHex = string.Empty;
            IsInherited = true;
            Close();
            return;
        }

        _apply(option.Hex);
        SelectedHex = option.Hex;
        IsInherited = false;
        _recentColors.Record(option.Hex);
        Close();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
