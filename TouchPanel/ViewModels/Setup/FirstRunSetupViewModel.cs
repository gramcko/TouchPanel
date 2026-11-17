using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Services.Monitors;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.ViewModels.Setup;

/// <summary>
/// Presentation state for the first-run setup wizard: the discovered monitors with their friendly
/// name, resolution, and primary marker, plus the start-with-Windows choice. Applying persists both
/// through the existing single-writer <see cref="ISettingsService"/> (<see cref="SettingsCoordinator"/>)
/// with one <see cref="SettingsChange"/>, so every other configuration section is preserved. WPF-free
/// and unit-testable without a desktop; all failures are logged and contained so startup continues.
/// </summary>
public sealed class FirstRunSetupViewModel : INotifyPropertyChanged
{
    private readonly IMonitorEnumerator _enumerator;
    private readonly ISettingsService _settings;
    private readonly ILogger _logger;

    private IReadOnlyList<MonitorPickerItem> _monitors = [];
    private MonitorPickerItem? _selectedMonitor;
    private bool _startWithWindows;
    private bool _isBusy;

    /// <summary>
    /// Initializes a new instance of the <see cref="FirstRunSetupViewModel"/> class.
    /// </summary>
    /// <param name="enumerator">The monitor discovery boundary (production: <c>Win32MonitorEnumerator</c>).</param>
    /// <param name="settings">The single-writer settings apply boundary.</param>
    /// <param name="startWithWindows">The initial start-with-Windows state to show in the checkbox.</param>
    /// <param name="logger">Logger for summary-only messages; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public FirstRunSetupViewModel(
        IMonitorEnumerator enumerator,
        ISettingsService settings,
        bool startWithWindows = false,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(settings);

        _enumerator = enumerator;
        _settings = settings;
        _startWithWindows = startWithWindows;
        _logger = (logger ?? Log.Logger).ForContext<FirstRunSetupViewModel>();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the discovered monitors with their display name, identity summary, and primary marker.</summary>
    public IReadOnlyList<MonitorPickerItem> Monitors
    {
        get => _monitors;
        private set
        {
            if (SetProperty(ref _monitors, value))
            {
                OnPropertyChanged(nameof(HasMonitors));
            }
        }
    }

    /// <summary>Gets a value indicating whether at least one monitor was discovered.</summary>
    public bool HasMonitors => _monitors.Count > 0;

    /// <summary>Gets or sets the monitor the panel should appear on; <see langword="null"/> when no monitor is available.</summary>
    public MonitorPickerItem? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (SetProperty(ref _selectedMonitor, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    /// <summary>Gets a value indicating whether a monitor is selected.</summary>
    public bool HasSelection => _selectedMonitor is not null;

    /// <summary>Gets or sets a value indicating whether TouchPanel should start with Windows.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    /// <summary>Gets a value indicating whether a discovery/apply operation is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Gets a value indicating whether the wizard choices were persisted successfully.</summary>
    public bool Applied { get; private set; }

    /// <summary>Gets a value indicating whether the user skipped the wizard.</summary>
    public bool Skipped { get; private set; }

    /// <summary>
    /// Enumerates the connected monitors off the UI thread and preselects the primary monitor (or
    /// the first discovered one). An enumeration failure or an empty result degrades to "no monitor
    /// choice", keeping the defaults while the start-with-Windows checkbox stays usable.
    /// </summary>
    /// <returns>A task that completes when the rows and default selection are ready.</returns>
    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            IReadOnlyList<MonitorDescriptor> discovered;
            try
            {
                discovered = await Task.Run(() => _enumerator.Enumerate() ?? []).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "First run: enumerating the connected monitors failed; the monitor choice is skipped");
                Monitors = [];
                SelectedMonitor = null;
                return;
            }

            var rows = new List<MonitorPickerItem>(discovered.Count);
            foreach (var monitor in discovered)
            {
                rows.Add(new MonitorPickerItem(
                    monitor,
                    monitor.DisplayName,
                    BuildMonitorSummary(monitor),
                    IsCurrent: false,
                    monitor.IsPrimary,
                    MonitorPickerViewModel.HasStableIdentity(monitor)));
            }

            Monitors = rows;
            SelectedMonitor = rows.FirstOrDefault(row => row.IsPrimary) ?? rows.FirstOrDefault();
            _logger.Information(
                "First run: monitor discovery completed. MonitorCount={MonitorCount} DefaultSelection={DefaultSelection}",
                rows.Count,
                SelectedMonitor?.DisplayName ?? "none");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Persists the selected monitor identity (and <c>monitor.fallback = none</c>) together with the
    /// start-with-Windows choice through the single-writer settings path. The <see
    /// cref="SettingsCoordinator"/> applies the HKCU Run registration before saving. Failures are
    /// logged and contained: the running defaults stay in place and startup continues.
    /// </summary>
    /// <returns><see langword="true"/> when the choices were saved and applied; otherwise <see langword="false"/>.</returns>
    public async Task<bool> ApplyAsync()
    {
        IsBusy = true;
        try
        {
            var selected = SelectedMonitor;
            var change = new SettingsChange
            {
                // An empty discovery leaves the default monitor block untouched (still usable
                // through the startup checkbox).
                SetMonitorMatch = selected is not null,
                MonitorMatch = selected is null ? null : MonitorPickerViewModel.BuildIdentity(selected.Monitor),
                MonitorFallback = selected is null ? null : MonitorFallback.None,
                StartWithWindows = StartWithWindows,
            };

            SettingsApplyResult result;
            try
            {
                result = await _settings.ApplyAsync(change).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "First run: saving the setup failed unexpectedly; the defaults are kept");
                return false;
            }

            if (!result.Succeeded)
            {
                _logger.Warning(
                    "First run: the setup could not be saved; the defaults are kept. Code={Code}",
                    result.ErrorCode);
                return false;
            }

            Applied = true;
            _logger.Information(
                "First run: setup saved. Monitor={Monitor} StartWithWindows={StartWithWindows}",
                selected?.DisplayName ?? "default",
                StartWithWindows);
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Records that the user skipped the wizard. Nothing is saved, so startup continues with the
    /// generated defaults (unmatched monitor, primary placement).
    /// </summary>
    public void Skip()
    {
        Skipped = true;
        _logger.Information("First run: setup was skipped; the default monitor and startup settings are kept");
    }

    private static string BuildMonitorSummary(MonitorDescriptor monitor)
    {
        var parts = new List<string>(3)
        {
            monitor.Bounds.IsEmpty
                ? "resolution unknown"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{monitor.Bounds.Width}×{monitor.Bounds.Height}"),
        };

        if (!string.IsNullOrWhiteSpace(monitor.HardwareId))
        {
            parts.Add(monitor.HardwareId!);
        }

        if (monitor.IsPrimary)
        {
            parts.Add("primary");
        }

        return string.Join(" · ", parts);
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
}
