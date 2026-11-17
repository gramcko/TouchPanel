using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Theme;

namespace TouchPanel.ViewModels.Settings;

/// <summary>
/// Presentation state for the "Appearance" section of the settings window (M3-RF-850). It exposes
/// the built-in theme and icon-pack allowlists plus an accent color field, validates the entries
/// with the same rules the configuration validator uses, and persists/applies through the existing
/// single-writer <see cref="ISettingsService"/> path (no new persistence mechanism). Theme and pack
/// selection apply immediately; the accent is validated before it is applied, and an invalid entry
/// is blocked inline (M3-RF-850/M3-RF-847).
/// </summary>
/// <remarks>
/// <b>Failure isolation (M3-RF-850/M3-RNF-138).</b> When an apply fails the UI reverts to the last
/// successfully applied values and surfaces the reason; the running appearance is unchanged because
/// the single-writer path never mutates the running configuration before a successful save
/// (M2-RF-922).
/// </remarks>
public sealed class AppearanceEditorViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settings;
    private readonly Func<AppearanceConfig?> _current;
    private readonly ILogger _logger;

    private string _selectedTheme;
    private string _selectedIconPack;
    private string _selectedPageTransition;
    private string _accentText;
    private string? _appliedAccent;
    private string? _lastTheme;
    private string? _lastIconPack;
    private string? _lastPageTransition;
    private string? _lastAccent;
    private bool _isBusy;
    private bool _applying;
    private string? _statusMessage;
    private bool _statusIsError;
    private string? _accentError;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppearanceEditorViewModel"/> class, seeding the
    /// selection from the currently running appearance.
    /// </summary>
    /// <param name="settings">The single-writer apply boundary; must not be <see langword="null"/>.</param>
    /// <param name="current">Returns the currently running appearance section; must not be <see langword="null"/>.</param>
    /// <param name="logger">Logger for summary-only messages; defaults to the global Serilog logger.</param>
    /// <param name="currentWindow">
    /// Returns the currently running window section so the page-transition dropdown seeds from the
    /// live value (user-requested addition); <see langword="null"/> seeds the documented default.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public AppearanceEditorViewModel(
        ISettingsService settings,
        Func<AppearanceConfig?> current,
        ILogger? logger = null,
        Func<WindowConfig?>? currentWindow = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _logger = (logger ?? Log.Logger).ForContext<AppearanceEditorViewModel>();

        ThemeOptions = ThemeIds.All;
        IconPackOptions = IconPackIds.All;
        PageTransitionOptions = PageTransitionModes.All;

        var appearance = current() ?? new AppearanceConfig();
        _lastTheme = ThemeIds.Normalize(appearance.Theme);
        _lastIconPack = IconPackIds.Normalize(appearance.IconPack);
        _lastAccent = string.IsNullOrWhiteSpace(appearance.AccentColor) ? null : appearance.AccentColor;
        _lastPageTransition = PageTransitionModes.Normalize(currentWindow?.Invoke()?.PageTransition);
        _selectedTheme = _lastTheme;
        _selectedIconPack = _lastIconPack;
        _selectedPageTransition = _lastPageTransition;
        _appliedAccent = _lastAccent;
        _accentText = _lastAccent ?? string.Empty;

        SetAccentCommand = new AsyncRelayCommand(SetAccentAsync);
        ClearAccentCommand = new AsyncRelayCommand(ClearAccentAsync);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the built-in theme ids in a stable order (M3-RF-842).</summary>
    public IReadOnlyList<string> ThemeOptions { get; }

    /// <summary>Gets the built-in icon-pack ids in a stable order (M3-RF-844).</summary>
    public IReadOnlyList<string> IconPackOptions { get; }

    /// <summary>
    /// Gets the page-transition ids offered by the "Page transition" dropdown (user-requested
    /// addition): <c>none</c>, <c>slide</c>, <c>fade</c>, <c>slideUp</c>, and <c>zoom</c>.
    /// <c>none</c> is the only option that disables transitions; every animated selection always
    /// plays on page changes (the OS animation-effects preference does not override it).
    /// </summary>
    public IReadOnlyList<string> PageTransitionOptions { get; }

    /// <summary>
    /// Gets or sets the selected normal-mode page transition; a change applies immediately through
    /// the shared single-writer settings path (user-requested addition). Unknown input is normalized
    /// to <c>slide</c>, exactly like the configuration validator. An explicit selection always
    /// animates on the next page change; only <c>none</c> turns transitions off.
    /// </summary>
    public string SelectedPageTransition
    {
        get => _selectedPageTransition;
        set
        {
            var normalized = PageTransitionModes.Normalize(value);
            if (SetField(ref _selectedPageTransition, normalized))
            {
                _ = ApplySelectionAsync();
            }
        }
    }

    /// <summary>Gets or sets the selected theme; a change applies immediately (M3-RF-850).</summary>
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            var normalized = ThemeIds.Normalize(value);
            if (SetField(ref _selectedTheme, normalized))
            {
                _ = ApplySelectionAsync();
            }
        }
    }

    /// <summary>Gets or sets the selected icon pack; a change applies immediately (M3-RF-850).</summary>
    public string SelectedIconPack
    {
        get => _selectedIconPack;
        set
        {
            var normalized = IconPackIds.Normalize(value);
            if (SetField(ref _selectedIconPack, normalized))
            {
                _ = ApplySelectionAsync();
            }
        }
    }

    /// <summary>Gets or sets the accent color text field (<c>#RRGGBB</c>/<c>#AARRGGBB</c>).</summary>
    public string AccentText
    {
        get => _accentText;
        set
        {
            if (SetField(ref _accentText, value ?? string.Empty))
            {
                AccentError = null;
            }
        }
    }

    /// <summary>Gets the inline accent validation message, or <see langword="null"/> when acceptable.</summary>
    public string? AccentError
    {
        get => _accentError;
        private set
        {
            if (SetField(ref _accentError, value))
            {
                OnPropertyChanged(nameof(HasAccentError));
            }
        }
    }

    /// <summary>Gets a value indicating whether an inline accent error is shown.</summary>
    public bool HasAccentError => !string.IsNullOrEmpty(_accentError);

    /// <summary>Gets a value indicating whether an apply is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>Gets the latest status line, or <see langword="null"/> when idle.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetField(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    /// <summary>Gets a value indicating whether a status line is present.</summary>
    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    /// <summary>Gets a value indicating whether the latest status line is a failure.</summary>
    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetField(ref _statusIsError, value);
    }

    /// <summary>Gets the command that validates and applies the accent color field (M3-RF-850).</summary>
    public ICommand SetAccentCommand { get; }

    /// <summary>Gets the command that clears the accent override (theme accent applies) (M3-RF-850).</summary>
    public ICommand ClearAccentCommand { get; }

    private async Task SetAccentAsync()
    {
        // M3-RF-847/M3-RF-850: invalid accent input is blocked inline, never applied.
        if (!WidgetAppearance.TryNormalizeColor(_accentText, out var normalized))
        {
            AccentError = "Enter a color as #RRGGBB or #AARRGGBB, or clear it to use the theme accent.";
            return;
        }

        AccentError = null;
        _appliedAccent = normalized;
        await ApplyAsync(_selectedTheme, _selectedIconPack, normalized, _selectedPageTransition).ConfigureAwait(true);
    }

    private async Task ClearAccentAsync()
    {
        AccentError = null;
        SetField(ref _accentText, string.Empty, nameof(AccentText));
        _appliedAccent = null;
        await ApplyAsync(_selectedTheme, _selectedIconPack, accent: null, _selectedPageTransition).ConfigureAwait(true);
    }

    private Task ApplySelectionAsync() =>
        ApplyAsync(_selectedTheme, _selectedIconPack, _appliedAccent, _selectedPageTransition);

    /// <summary>
    /// Test seam (M3-RF-850): validates and applies the accent color field. The
    /// <see cref="SetAccentCommand"/> invokes this; an invalid value is blocked inline and the running
    /// appearance is unchanged.
    /// </summary>
    /// <returns>A task that completes when the apply attempt finished.</returns>
    internal Task ApplyAccentAsync() => SetAccentAsync();

    /// <summary>Test seam (M3-RF-850): clears the accent override and applies the theme accent.</summary>
    /// <returns>A task that completes when the apply attempt finished.</returns>
    internal Task ClearAccentInternalAsync() => ClearAccentAsync();

    /// <summary>Test seam (M3-RF-850): applies the currently selected theme/pack/accent.</summary>
    /// <returns>A task that completes when the apply attempt finished.</returns>
    internal Task ApplySelectionInternalAsync() => ApplySelectionAsync();

    private async Task ApplyAsync(string theme, string iconPack, string? accent, string pageTransition)
    {
        if (_applying)
        {
            return;
        }

        // Initial data binding assigns the running values; applying them would be a redundant save.
        if (string.Equals(theme, _lastTheme, StringComparison.Ordinal)
            && string.Equals(iconPack, _lastIconPack, StringComparison.Ordinal)
            && string.Equals(accent, _lastAccent, StringComparison.Ordinal)
            && string.Equals(pageTransition, _lastPageTransition, StringComparison.Ordinal))
        {
            return;
        }

        _applying = true;
        IsBusy = true;
        try
        {
            var next = new AppearanceConfig
            {
                Theme = theme,
                AccentColor = accent,
                IconPack = iconPack,
            };

            var result = await _settings
                .ApplyAsync(new SettingsChange { Appearance = next, PageTransition = pageTransition })
                .ConfigureAwait(true);

            if (result.Succeeded)
            {
                _lastTheme = theme;
                _lastIconPack = iconPack;
                _lastAccent = accent;
                _lastPageTransition = pageTransition;
                SetStatus("Appearance applied.", isError: false);
            }
            else
            {
                SetStatus(result.ErrorMessage ?? "The appearance could not be applied.", isError: true);
                Revert();
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Appearance editor: applying the appearance change failed unexpectedly");
            SetStatus("The appearance could not be applied.", isError: true);
            Revert();
        }
        finally
        {
            _applying = false;
            IsBusy = false;
        }
    }

    private void Revert()
    {
        SetField(ref _selectedTheme, _lastTheme ?? ThemeIds.Dark, nameof(SelectedTheme));
        SetField(ref _selectedIconPack, _lastIconPack ?? IconPackIds.Default, nameof(SelectedIconPack));
        SetField(
            ref _selectedPageTransition,
            _lastPageTransition ?? PageTransitionModes.Default,
            nameof(SelectedPageTransition));
        _appliedAccent = _lastAccent;
        SetField(ref _accentText, _lastAccent ?? string.Empty, nameof(AccentText));
    }

    private void SetStatus(string? message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
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
