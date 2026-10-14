using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.ImportExport;
using TouchPanel.Services.Config;

namespace TouchPanel.ViewModels.Settings;

/// <summary>
/// Presentation state for the tray-accessible settings window (T-17, RF-092/M2-RF-921..925). It
/// exposes the M2-editable options — <c>alwaysOnTop</c>, start-with-Windows, the telemetry polling
/// interval, and import/export — plus read-only diagnostics (configuration path, schema version,
/// current monitor identity). Every accepted change is applied live through
/// <see cref="ISettingsService"/> (the shared store + apply pipeline); invalid input is blocked
/// inline and never reaches the store (M2-RF-922). Hotkey editing is intentionally absent
/// (RF-061/M3). The view model is WPF-free apart from notifications and is unit-testable without a
/// desktop (M2-RNF-124).
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settings;
    private readonly Func<PanelConfig?> _configAccessor;
    private readonly ConfigImportExport? _importExport;
    private readonly IConfigFilePicker _filePicker;
    private readonly ILogger _logger;

    private bool _alwaysOnTop;
    private bool _startWithWindows;
    private double _pollIntervalSeconds = TelemetryConfig.DefaultPollIntervalSeconds;
    private bool _isBusy;
    private string? _statusMessage;
    private bool _statusIsError;
    private string? _intervalError;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="settings">The apply boundary (store + shared apply pipeline); must not be <see langword="null"/>.</param>
    /// <param name="configAccessor">Returns the currently running configuration for initial values and diagnostics.</param>
    /// <param name="importExport">The import/export service, or <see langword="null"/> until the file-backed configuration is live.</param>
    /// <param name="filePicker">The import/export file-picker seam; must not be <see langword="null"/>.</param>
    /// <param name="configPath">The <c>panel.json</c> path shown as a read-only diagnostic.</param>
    /// <param name="logger">Logger for summary-only messages; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public SettingsViewModel(
        ISettingsService settings,
        Func<PanelConfig?> configAccessor,
        ConfigImportExport? importExport,
        IConfigFilePicker filePicker,
        string configPath,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(configAccessor);
        ArgumentNullException.ThrowIfNull(filePicker);

        _settings = settings;
        _configAccessor = configAccessor;
        _importExport = importExport;
        _filePicker = filePicker;
        ConfigPath = configPath ?? string.Empty;
        _logger = (logger ?? Log.Logger).ForContext<SettingsViewModel>();

        ReloadFromConfig();

        SetAlwaysOnTopCommand = new BoolAsyncCommand(SetAlwaysOnTopAsync);
        SetStartWithWindowsCommand = new BoolAsyncCommand(SetStartWithWindowsAsync);
        ApplyIntervalCommand = new AsyncRelayCommand(ApplyIntervalAsync);
        ImportCommand = new AsyncRelayCommand(ImportAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets a value indicating whether the panel stays above other windows (<c>window.alwaysOnTop</c>).</summary>
    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        private set => SetProperty(ref _alwaysOnTop, value);
    }

    /// <summary>Gets a value indicating whether the per-user start-with-Windows registration is enabled.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        private set => SetProperty(ref _startWithWindows, value);
    }

    /// <summary>
    /// Gets or sets the draft telemetry polling interval in seconds. Setting it only updates the
    /// draft; <see cref="ApplyIntervalCommand"/> validates and applies it (M2-RF-922).
    /// </summary>
    public double PollIntervalSeconds
    {
        get => _pollIntervalSeconds;
        set
        {
            if (SetProperty(ref _pollIntervalSeconds, value))
            {
                IntervalError = null;
            }
        }
    }

    /// <summary>Gets the lowest accepted polling interval in seconds (0.5 s), for the slider bounds.</summary>
    public double MinimumPollIntervalSeconds => TelemetryConfig.MinPollIntervalSeconds;

    /// <summary>Gets the highest accepted polling interval in seconds (5 s), for the slider bounds.</summary>
    public double MaximumPollIntervalSeconds => TelemetryConfig.MaxPollIntervalSeconds;

    /// <summary>Gets the read-only configuration file path diagnostic.</summary>
    public string ConfigPath { get; }

    /// <summary>Gets the read-only schema-version diagnostic text.</summary>
    public string SchemaVersionText =>
        (_configAccessor()?.SchemaVersion ?? 0).ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets the read-only current-target-monitor diagnostic text.</summary>
    public string CurrentMonitorText
    {
        get
        {
            var match = _configAccessor()?.Monitor.Match;
            if (match is null)
            {
                return "Not configured";
            }

            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(match.HardwareId))
            {
                parts.Add(match.HardwareId!);
            }

            if (!string.IsNullOrWhiteSpace(match.DevicePath))
            {
                parts.Add(match.DevicePath!);
            }

            if (!string.IsNullOrWhiteSpace(match.FriendlyName))
            {
                parts.Add(match.FriendlyName!);
            }

            return parts.Count > 0 ? string.Join(" · ", parts) : "Not configured";
        }
    }

    /// <summary>Gets a value indicating whether the import/export buttons are available.</summary>
    public bool CanImportExport => _importExport is not null;

    /// <summary>Gets a value indicating whether a settings operation is currently running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Gets the latest status line (success or failure), or <see langword="null"/> when idle.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    /// <summary>Gets a value indicating whether a status line is present.</summary>
    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    /// <summary>Gets a value indicating whether the latest status line represents a failure.</summary>
    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    /// <summary>Gets the inline validation error for the interval draft, or <see langword="null"/> when it is valid.</summary>
    public string? IntervalError
    {
        get => _intervalError;
        private set
        {
            if (SetProperty(ref _intervalError, value))
            {
                OnPropertyChanged(nameof(HasIntervalError));
            }
        }
    }

    /// <summary>Gets a value indicating whether the interval draft is invalid (blocked inline).</summary>
    public bool HasIntervalError => !string.IsNullOrEmpty(_intervalError);

    /// <summary>Gets the command that applies the <c>alwaysOnTop</c> toggle (parameter: the new value).</summary>
    public ICommand SetAlwaysOnTopCommand { get; }

    /// <summary>Gets the command that applies the start-with-Windows toggle (parameter: the new value).</summary>
    public ICommand SetStartWithWindowsCommand { get; }

    /// <summary>Gets the command that validates and applies the telemetry interval draft.</summary>
    public ICommand ApplyIntervalCommand { get; }

    /// <summary>Gets the command that imports a configuration file chosen through the picker.</summary>
    public ICommand ImportCommand { get; }

    /// <summary>Gets the command that exports the running configuration to a chosen file.</summary>
    public ICommand ExportCommand { get; }

    /// <summary>
    /// Applies the <c>alwaysOnTop</c> change through the shared store + apply pipeline (M2-RF-923).
    /// The toggle is only reflected once the change was saved and applied; a failure reverts it.
    /// </summary>
    /// <param name="value">The requested value.</param>
    /// <returns><see langword="true"/> when the change applied; otherwise <see langword="false"/>.</returns>
    public Task<bool> SetAlwaysOnTopAsync(bool value) =>
        ApplyAsync(new SettingsChange { AlwaysOnTop = value }, () => AlwaysOnTop = value);

    /// <summary>
    /// Applies the start-with-Windows change through the existing M1 registration (M2-RF-923). The
    /// toggle is only reflected once the registration succeeded and the value persisted.
    /// </summary>
    /// <param name="value">The requested value.</param>
    /// <returns><see langword="true"/> when the change applied; otherwise <see langword="false"/>.</returns>
    public Task<bool> SetStartWithWindowsAsync(bool value) =>
        ApplyAsync(new SettingsChange { StartWithWindows = value }, () => StartWithWindows = value);

    /// <summary>
    /// Validates the interval draft (0.5–5 s) and, when valid, applies it live through the shared
    /// pipeline. Out-of-range or non-finite input is blocked inline and never reaches the store
    /// (M2-RF-922).
    /// </summary>
    /// <returns><see langword="true"/> when the interval applied; otherwise <see langword="false"/>.</returns>
    public async Task<bool> ApplyIntervalAsync()
    {
        var draft = PollIntervalSeconds;
        if (!double.IsFinite(draft)
            || draft < TelemetryConfig.MinPollIntervalSeconds
            || draft > TelemetryConfig.MaxPollIntervalSeconds)
        {
            IntervalError = string.Create(
                CultureInfo.InvariantCulture,
                $"Enter a value between {TelemetryConfig.MinPollIntervalSeconds:0.#} and {TelemetryConfig.MaxPollIntervalSeconds:0.#} seconds.");
            SetStatus("The polling interval was not applied because it is out of range.", isError: true);
            return false;
        }

        var applied = await ApplyAsync(
            new SettingsChange { PollIntervalSeconds = draft },
            () =>
            {
                PollIntervalSeconds = _configAccessor()?.Telemetry.PollIntervalSeconds ?? draft;
                IntervalError = null;
            }).ConfigureAwait(true);

        if (!applied)
        {
            IntervalError = "The polling interval could not be applied.";
        }

        return applied;
    }

    /// <summary>
    /// Exports the running configuration to a picker-chosen path (M2-RF-831). The file I/O runs off
    /// the UI thread inside <see cref="ConfigImportExport"/>; only paths and summaries are shown.
    /// </summary>
    /// <returns><see langword="true"/> when the export succeeded; otherwise <see langword="false"/>.</returns>
    public async Task<bool> ExportAsync()
    {
        var importExport = _importExport;
        if (importExport is null)
        {
            SetStatus("Configuration import/export is not available yet.", isError: true);
            return false;
        }

        var path = _filePicker.PickExportPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("Export canceled.", isError: false);
            return false;
        }

        IsBusy = true;
        try
        {
            var result = await importExport.ExportAsync(path).ConfigureAwait(true);
            if (result.Succeeded)
            {
                SetStatus($"Configuration exported to '{result.Path}'.", isError: false);
                return true;
            }

            SetStatus(result.ErrorMessage ?? "The configuration could not be exported.", isError: true);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Settings: the export action failed unexpectedly");
            SetStatus("The configuration could not be exported.", isError: true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Imports a picker-chosen configuration file (M2-RF-832/M2-RF-833): validate, pre-import backup,
    /// persist, and apply. A failed import changes nothing; only paths and summaries are shown.
    /// </summary>
    /// <returns><see langword="true"/> when the import succeeded; otherwise <see langword="false"/>.</returns>
    public async Task<bool> ImportAsync()
    {
        var importExport = _importExport;
        if (importExport is null)
        {
            SetStatus("Configuration import/export is not available yet.", isError: true);
            return false;
        }

        var path = _filePicker.PickImportPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("Import canceled.", isError: false);
            return false;
        }

        IsBusy = true;
        try
        {
            var result = await importExport.ImportAsync(path).ConfigureAwait(true);
            if (result.Succeeded)
            {
                ReloadFromConfig();
                SetStatus($"Configuration imported from '{result.Path}'.", isError: false);
                return true;
            }

            SetStatus(result.ErrorMessage ?? "The selected configuration was not imported.", isError: true);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Settings: the import action failed unexpectedly");
            SetStatus("The selected configuration was not imported.", isError: true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Refreshes the reflected values and diagnostics from the running configuration (used after an
    /// import or an external reload, M2-RF-924).
    /// </summary>
    public void ReloadFromConfig()
    {
        var config = _configAccessor();
        AlwaysOnTop = config?.Window.AlwaysOnTop ?? false;
        StartWithWindows = config?.Startup.StartWithWindows ?? false;
        PollIntervalSeconds = config?.Telemetry.PollIntervalSeconds ?? TelemetryConfig.DefaultPollIntervalSeconds;
        IntervalError = null;
        OnPropertyChanged(nameof(SchemaVersionText));
        OnPropertyChanged(nameof(CurrentMonitorText));
        OnPropertyChanged(nameof(CanImportExport));
    }

    private async Task<bool> ApplyAsync(SettingsChange change, Action onSuccess)
    {
        IsBusy = true;
        try
        {
            var result = await _settings.ApplyAsync(change).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                SetStatus(result.ErrorMessage ?? "The setting could not be applied.", isError: true);
                return false;
            }

            onSuccess();
            SetStatus("Settings applied.", isError: false);
            OnPropertyChanged(nameof(CurrentMonitorText));
            OnPropertyChanged(nameof(SchemaVersionText));
            return true;
        }
        catch (Exception ex)
        {
            // M2-RF-925/RF-012: a settings failure must never take the window or the panel down.
            _logger.Warning(ex, "Settings: applying a change failed unexpectedly");
            SetStatus("The setting could not be applied.", isError: true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStatus(string? message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

    /// <summary>
    /// Parameterized asynchronous command carrying a <see cref="bool"/> toggle value (MVVM: the
    /// control passes its new checked state as the parameter). Failures are contained here as a
    /// last-resort guard; the view model already converts them into a status line (M2-RF-925).
    /// </summary>
    private sealed class BoolAsyncCommand : ICommand
    {
        private readonly Func<bool, Task> _execute;

        public BoolAsyncCommand(Func<bool, Task> execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is bool;

        public async void Execute(object? parameter)
        {
            if (parameter is not bool value)
            {
                return;
            }

            try
            {
                await _execute(value).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Never surface an exception to the dispatcher.
            }
        }
    }
}
