using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TouchPanel.Config;
using TouchPanel.Views.Controls;

namespace TouchPanel.ViewModels.Edit;

/// <summary>Which field opened the property-panel flyout.</summary>
public enum PropertyPickerKind
{
    /// <summary>No flyout is open.</summary>
    None,

    /// <summary>The icon flyout is open.</summary>
    Icon,

    /// <summary>The colour flyout is open.</summary>
    Color,
}

/// <summary>The appearance field targeted by the open property-panel flyout.</summary>
public enum PropertyPickerTarget
{
    /// <summary>No target (the flyout is closed).</summary>
    None,

    /// <summary>The primary widget icon.</summary>
    Icon,

    /// <summary>The label-replacement icon.</summary>
    LabelIcon,

    /// <summary>The toggle's on-state icon.</summary>
    OnIcon,

    /// <summary>The foreground colour.</summary>
    Foreground,

    /// <summary>The background colour.</summary>
    Background,

    /// <summary>The accent colour.</summary>
    Accent,

    /// <summary>The toggle's on-state icon colour.</summary>
    OnIconColor,
}

/// <summary>
/// State and behaviour of the property panel's docked picker flyout (user-requested replacement for
/// inline expansion). One flyout serves both the icon fields (Icon, Label icon, On icon) and the
/// colour fields (Foreground, Background, Accent, On icon colour, page background), so the two kinds
/// are mutually exclusive and switching fields retargets the open flyout in place.
/// </summary>
/// <remarks>
/// The flyout lives outside the property panel's scroll surface; opening it never changes the panel
/// layout. All writes go through the editor's existing properties, so each selection keeps the
/// ordinary apply/live-projection path and produces exactly one undo step. Colour selections are
/// recorded in the shared <see cref="IRecentColorPalette"/> (MRU first) before the flyout closes.
/// </remarks>
public sealed class PropertyPickerFlyoutViewModel : INotifyPropertyChanged
{
    /// <summary>Message shown when the exact-hex input cannot be parsed.</summary>
    public const string InvalidColorMessage = "Color must be #RGB, #RRGGBB, or #AARRGGBB.";

    private readonly PropertyEditorViewModel _editor;
    private readonly IRecentColorPalette _recentColors;
    private PropertyPickerKind _kind;
    private PropertyPickerTarget _target;
    private IReadOnlyList<IconOption> _iconOptions = [];
    private IReadOnlyList<ColorSwatchOption> _colorTiles = [];
    private IconOption? _selectedIcon;
    private string _selectedHex = string.Empty;
    private bool _isInherited;
    private string _hexError = string.Empty;
    private bool _isOpen;

    /// <summary>Initializes a new instance of the <see cref="PropertyPickerFlyoutViewModel"/> class.</summary>
    /// <param name="editor">The owning property editor; must not be <see langword="null"/>.</param>
    /// <param name="recentColors">
    /// The shared most-recent colour list; when <see langword="null"/>, an in-memory palette seeded
    /// with the defaults is used (tests and design time never touch <c>state.json</c>).
    /// </param>
    public PropertyPickerFlyoutViewModel(
        PropertyEditorViewModel editor,
        IRecentColorPalette? recentColors = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
        _recentColors = recentColors ?? new RecentColorPalette();

        SelectIconCommand = new RelayCommand<IconOption>(SelectIcon);
        SelectColorCommand = new RelayCommand<ColorSwatchOption>(SelectColor);
        CloseCommand = new RelayCommand(Close);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the command that selects an icon tile and closes the flyout.</summary>
    public System.Windows.Input.ICommand SelectIconCommand { get; }

    /// <summary>Gets the command that selects a colour (or "None") tile and closes the flyout.</summary>
    public System.Windows.Input.ICommand SelectColorCommand { get; }

    /// <summary>Gets the command that closes the flyout.</summary>
    public System.Windows.Input.ICommand CloseCommand { get; }

    /// <summary>Gets a value indicating whether the flyout is open.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    /// <summary>Gets which kind of flyout is open.</summary>
    public PropertyPickerKind Kind
    {
        get => _kind;
        private set
        {
            if (SetField(ref _kind, value))
            {
                OnPropertyChanged(nameof(IsIconFlyout));
                OnPropertyChanged(nameof(IsColorFlyout));
            }
        }
    }

    /// <summary>Gets the field the open flyout targets.</summary>
    public PropertyPickerTarget Target
    {
        get => _target;
        private set => SetField(ref _target, value);
    }

    /// <summary>Gets a value indicating whether the icon flyout is active.</summary>
    public bool IsIconFlyout => IsOpen && Kind == PropertyPickerKind.Icon;

    /// <summary>Gets a value indicating whether the colour flyout is active.</summary>
    public bool IsColorFlyout => IsOpen && Kind == PropertyPickerKind.Color;

    /// <summary>Gets the flyout heading (the field label).</summary>
    public string Title => Target switch
    {
        PropertyPickerTarget.Icon => "Icon",
        PropertyPickerTarget.LabelIcon => "Label icon",
        PropertyPickerTarget.OnIcon => "On icon",
        PropertyPickerTarget.Foreground => "Foreground",
        PropertyPickerTarget.Background => "Background",
        PropertyPickerTarget.Accent => "Accent",
        PropertyPickerTarget.OnIconColor => "On icon colour",
        _ => string.Empty,
    };

    /// <summary>Gets the icons offered by the icon flyout (none plus the whole built-in catalog).</summary>
    public IReadOnlyList<IconOption> IconOptions
    {
        get => _iconOptions;
        private set => SetField(ref _iconOptions, value);
    }

    /// <summary>Gets the colour tiles offered by the colour flyout (MRU first, then the rest).</summary>
    public IReadOnlyList<ColorSwatchOption> ColorTiles
    {
        get => _colorTiles;
        private set => SetField(ref _colorTiles, value);
    }

    /// <summary>Gets the currently used icon (marked in the grid).</summary>
    public IconOption? SelectedIcon
    {
        get => _selectedIcon;
        private set => SetField(ref _selectedIcon, value);
    }

    /// <summary>Gets the current colour as normalized hex, or an empty string when unset.</summary>
    public string SelectedHex
    {
        get => _selectedHex;
        private set => SetField(ref _selectedHex, value);
    }

    /// <summary>Gets a value indicating whether the current colour field is inherited/unset.</summary>
    public bool IsInherited
    {
        get => _isInherited;
        private set => SetField(ref _isInherited, value);
    }

    /// <summary>Gets a value indicating whether the "None" tile is offered for the current field.</summary>
    public bool AllowNone => IsColorTarget(Target);

    /// <summary>Gets the latest exact-hex validation message, or an empty string.</summary>
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

    /// <summary>
    /// Toggles the icon flyout for <paramref name="target"/>: tapping the same field again closes it,
    /// tapping another icon field switches the target in place.
    /// </summary>
    /// <param name="target">The icon field (Icon, LabelIcon, or OnIcon).</param>
    public void ToggleIcon(PropertyPickerTarget target)
    {
        if (IsOpen && Kind == PropertyPickerKind.Icon && Target == target)
        {
            Close();
            return;
        }

        OpenIcon(target);
    }

    /// <summary>
    /// Toggles the colour flyout for <paramref name="target"/>: tapping the same field again closes
    /// it, tapping another colour field switches the target in place. Either kind hides the other.
    /// </summary>
    /// <param name="target">The colour field (Foreground, Background, Accent, or OnIconColor).</param>
    public void ToggleColor(PropertyPickerTarget target)
    {
        if (IsOpen && Kind == PropertyPickerKind.Color && Target == target)
        {
            Close();
            return;
        }

        OpenColor(target);
    }

    /// <summary>Opens the icon flyout for <paramref name="target"/> (no-op for unsupported fields).</summary>
    /// <param name="target">The icon field.</param>
    public void OpenIcon(PropertyPickerTarget target)
    {
        if (!TryReadIconTarget(target, out var options, out var selected))
        {
            return;
        }

        Kind = PropertyPickerKind.Icon;
        Target = target;
        IconOptions = options;
        SelectedIcon = selected;
        HexError = string.Empty;
        IsOpen = true;
        RaiseTargetProperties();
    }

    /// <summary>Opens the colour flyout for <paramref name="target"/> (no-op for unsupported fields).</summary>
    /// <param name="target">The colour field.</param>
    public void OpenColor(PropertyPickerTarget target)
    {
        if (!IsColorTarget(target))
        {
            return;
        }

        Kind = PropertyPickerKind.Color;
        Target = target;
        ColorTiles = ColorSwatchOption.CreateTiles(_recentColors.Colors, allowNone: true);
        SelectedHex = ReadColor(target) ?? string.Empty;
        IsInherited = ReadColorInherited(target);
        HexError = string.Empty;
        IsOpen = true;
        RaiseTargetProperties();
    }

    /// <summary>Closes the flyout without changing the selection.</summary>
    public void Close()
    {
        if (!IsOpen && Kind == PropertyPickerKind.None)
        {
            return;
        }

        IsOpen = false;
        Kind = PropertyPickerKind.None;
        Target = PropertyPickerTarget.None;
        HexError = string.Empty;
        RaiseTargetProperties();
    }

    /// <summary>
    /// Applies an exact hex value (or "None" when blank and allowed) and closes the flyout. Invalid
    /// text is rejected with inline feedback and leaves the configuration untouched.
    /// </summary>
    /// <param name="text">The typed hex text.</param>
    /// <returns><see langword="true"/> when a value was applied.</returns>
    public bool TryCommitHex(string? text)
    {
        if (!IsColorTarget(Target))
        {
            return false;
        }

        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            if (!AllowNone)
            {
                HexError = InvalidColorMessage;
                return false;
            }

            ApplyColor(string.Empty, record: false);
            return true;
        }

        if (!ColorPalette.TryParseHex(value, out var normalized) || normalized is null)
        {
            HexError = InvalidColorMessage;
            return false;
        }

        ApplyColor(normalized, record: true);
        return true;
    }

    /// <summary>
    /// Refreshes the colour tiles from the shared MRU palette (used when the flyout is re-opened or
    /// after an external selection). Keeps the current target.
    /// </summary>
    public void RefreshColorTiles()
    {
        if (Kind == PropertyPickerKind.Color)
        {
            ColorTiles = ColorSwatchOption.CreateTiles(_recentColors.Colors, allowNone: true);
        }
    }

    private void SelectIcon(IconOption? option)
    {
        if (!IsIconTarget(Target) || option is null)
        {
            return;
        }

        SetIcon(Target, option);
        Close();
    }

    private void SelectColor(ColorSwatchOption? option)
    {
        if (!IsColorTarget(Target) || option is null)
        {
            return;
        }

        if (option.IsNone)
        {
            ApplyColor(string.Empty, record: false);
            return;
        }

        ApplyColor(option.Hex, record: true);
    }

    private void ApplyColor(string hex, bool record)
    {
        SetColor(Target, hex);
        SelectedHex = hex;
        IsInherited = hex.Length == 0;
        if (record)
        {
            _recentColors.Record(hex);
        }

        Close();
    }

    private bool TryReadIconTarget(
        PropertyPickerTarget target,
        out IReadOnlyList<IconOption> options,
        out IconOption selected)
    {
        options = [];
        selected = _editor.IconOptions.Count > 0 ? _editor.IconOptions[0] : new IconOption(string.Empty, string.Empty, "(none)");

        switch (target)
        {
            case PropertyPickerTarget.Icon when _editor.SupportsAppearance:
                options = _editor.IconOptions;
                selected = _editor.SelectedIcon;
                return true;
            case PropertyPickerTarget.LabelIcon when _editor.SupportsLabelIcon:
                options = _editor.LabelIconOptions;
                selected = _editor.SelectedLabelIcon;
                return true;
            case PropertyPickerTarget.OnIcon when _editor.SupportsOnIcon:
                options = _editor.OnIconOptions;
                selected = _editor.SelectedOnIcon;
                return true;
            default:
                return false;
        }
    }

    private void SetIcon(PropertyPickerTarget target, IconOption option)
    {
        switch (target)
        {
            case PropertyPickerTarget.Icon:
                _editor.SelectedIcon = option;
                break;
            case PropertyPickerTarget.LabelIcon:
                _editor.SelectedLabelIcon = option;
                break;
            case PropertyPickerTarget.OnIcon:
                _editor.SelectedOnIcon = option;
                break;
        }
    }

    private string? ReadColor(PropertyPickerTarget target) => target switch
    {
        PropertyPickerTarget.Foreground => _editor.ForegroundText,
        PropertyPickerTarget.Background => _editor.BackgroundText,
        PropertyPickerTarget.Accent => _editor.AccentText,
        PropertyPickerTarget.OnIconColor => _editor.OnIconColorText,
        _ => null,
    };

    private bool ReadColorInherited(PropertyPickerTarget target) => target switch
    {
        PropertyPickerTarget.Foreground => _editor.IsForegroundInherited,
        PropertyPickerTarget.Background => _editor.IsBackgroundInherited,
        PropertyPickerTarget.Accent => _editor.IsAccentInherited,
        PropertyPickerTarget.OnIconColor => _editor.IsOnIconColorInherited,
        _ => true,
    };

    private void SetColor(PropertyPickerTarget target, string hex)
    {
        switch (target)
        {
            case PropertyPickerTarget.Foreground:
                _editor.ForegroundText = hex;
                break;
            case PropertyPickerTarget.Background:
                _editor.BackgroundText = hex;
                break;
            case PropertyPickerTarget.Accent:
                _editor.AccentText = hex;
                break;
            case PropertyPickerTarget.OnIconColor:
                _editor.OnIconColorText = hex;
                break;
        }
    }

    private static bool IsIconTarget(PropertyPickerTarget target) =>
        target is PropertyPickerTarget.Icon or PropertyPickerTarget.LabelIcon or PropertyPickerTarget.OnIcon;

    private static bool IsColorTarget(PropertyPickerTarget target) =>
        target is PropertyPickerTarget.Foreground
            or PropertyPickerTarget.Background
            or PropertyPickerTarget.Accent
            or PropertyPickerTarget.OnIconColor;

    private void RaiseTargetProperties()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(AllowNone));
        OnPropertyChanged(nameof(IsIconFlyout));
        OnPropertyChanged(nameof(IsColorFlyout));
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
