using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Services.Monitors;
using TouchPanel.Services.Notifications;

namespace TouchPanel.ViewModels.Settings;

/// <summary>
/// One monitor offered by the picker (T-17, RF-093/M2-RF-931): the discovered descriptor, a
/// display label, the stable identity summary, and whether it is the current target / primary.
/// <see cref="HasStableIdentity"/> is <see langword="false"/> when the descriptor exposes no
/// identity field at all (M2-RF-934).
/// </summary>
/// <param name="Monitor">The discovered descriptor; the identity block is derived from it on selection.</param>
/// <param name="DisplayName">A human-readable label (friendly name, then hardware id, then GDI name).</param>
/// <param name="IdentitySummary">A short identity/resolution description for the row.</param>
/// <param name="IsCurrent">Whether the running configuration's identity block selects this monitor.</param>
/// <param name="IsPrimary">Whether this is the Windows primary monitor.</param>
/// <param name="HasStableIdentity">Whether at least one stable identity field is present.</param>
public sealed record MonitorPickerItem(
    MonitorDescriptor Monitor,
    string DisplayName,
    string IdentitySummary,
    bool IsCurrent,
    bool IsPrimary,
    bool HasStableIdentity);

/// <summary>
/// Presentation state for the monitor picker (T-17, RF-093/M2-RF-931..934). It lists every
/// discovered monitor with its stable identity fields, resolution, position, and primary flag,
/// highlights the current target, surfaces an unavailable target so the user can reselect, offers
/// the fallback policy, and persists a selection through <see cref="ISettingsService"/> (the single-
/// writer store + apply pipeline) without a restart. Discovery uses the existing
/// <see cref="IMonitorEnumerator"/>; a monitor with no stable identity is warned about before saving
/// (M2-RF-934). WPF-free apart from notifications and unit-testable without a desktop (M2-RNF-124).
/// </summary>
public sealed class MonitorPickerViewModel : INotifyPropertyChanged
{
    private readonly IMonitorEnumerator _enumerator;
    private readonly ISettingsService _settings;
    private readonly Func<MonitorConfig?> _monitorAccessor;
    private readonly IUserNotifier? _notifier;
    private readonly ILogger _logger;

    private IReadOnlyList<MonitorPickerItem> _monitors = [];
    private MonitorFallback _fallback = MonitorFallback.None;
    private string _targetStatusText = "No target monitor has been selected.";
    private bool _hasUnavailableTarget;
    private bool _isBusy;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitorPickerViewModel"/> class.
    /// </summary>
    /// <param name="enumerator">The monitor discovery boundary (production: <c>Win32MonitorEnumerator</c>).</param>
    /// <param name="settings">The apply boundary that persists the identity block and re-places the panel.</param>
    /// <param name="monitorAccessor">Returns the running configuration's monitor section for the current-target display.</param>
    /// <param name="notifier">Optional notification boundary for the "no stable identity" warning (M2-RF-934).</param>
    /// <param name="logger">Logger for summary-only messages; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public MonitorPickerViewModel(
        IMonitorEnumerator enumerator,
        ISettingsService settings,
        Func<MonitorConfig?> monitorAccessor,
        IUserNotifier? notifier = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitorAccessor);

        _enumerator = enumerator;
        _settings = settings;
        _monitorAccessor = monitorAccessor;
        _notifier = notifier;
        _logger = (logger ?? Log.Logger).ForContext<MonitorPickerViewModel>();

        _fallback = NormalizeFallback(monitorAccessor()?.Fallback ?? MonitorFallback.None);
        SelectMonitorCommand = new MonitorAsyncCommand(SelectAsync);
        SetFallbackCommand = new FallbackAsyncCommand(ApplyFallbackAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the discovered monitors with their identity/current/primary markers.</summary>
    public IReadOnlyList<MonitorPickerItem> Monitors
    {
        get => _monitors;
        private set => SetProperty(ref _monitors, value);
    }

    /// <summary>Gets or sets the fallback policy applied when the target monitor is unavailable (M2-RF-933).</summary>
    public MonitorFallback Fallback
    {
        get => _fallback;
        set => SetProperty(ref _fallback, NormalizeFallback(value));
    }

    /// <summary>Gets the current-target status text (highlighted match or an explicit unavailable warning).</summary>
    public string TargetStatusText
    {
        get => _targetStatusText;
        private set => SetProperty(ref _targetStatusText, value);
    }

    /// <summary>Gets a value indicating whether the configured target monitor could not be resolved (M2-RF-933).</summary>
    public bool HasUnavailableTarget
    {
        get => _hasUnavailableTarget;
        private set => SetProperty(ref _hasUnavailableTarget, value);
    }

    /// <summary>Gets a value indicating whether a discovery/apply operation is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Gets the command that selects a monitor (parameter: the <see cref="MonitorPickerItem"/>).</summary>
    public ICommand SelectMonitorCommand { get; }

    /// <summary>Gets the command that applies the fallback policy (parameter: a <see cref="MonitorFallback"/>).</summary>
    public ICommand SetFallbackCommand { get; }

    /// <summary>Gets the command that re-enumerates the connected monitors.</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>
    /// Builds the deterministic identity block for a descriptor (M2-RF-932/M2-RF-934): the unit-level
    /// hardware id (falling back to the model id), the display device path (falling back to the
    /// monitor device instance id), and the friendly name, each only when present.
    /// </summary>
    /// <param name="monitor">The descriptor to derive the identity from; must not be <see langword="null"/>.</param>
    /// <returns>The identity block; never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="monitor"/> is <see langword="null"/>.</exception>
    public static MonitorIdentity BuildIdentity(MonitorDescriptor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        return new MonitorIdentity
        {
            HardwareId = FirstNonEmpty(monitor.HardwareIdWithSerial, monitor.HardwareId),
            DevicePath = FirstNonEmpty(monitor.DevicePath, monitor.MonitorDeviceId),
            FriendlyName = FirstNonEmpty(monitor.FriendlyName),
        };
    }

    /// <summary>
    /// Whether a descriptor exposes at least one stable identity field; when it does not, the picker
    /// warns before saving because the resulting match cannot be deterministic (M2-RF-934).
    /// </summary>
    /// <param name="monitor">The descriptor to test; must not be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a stable identity field exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="monitor"/> is <see langword="null"/>.</exception>
    public static bool HasStableIdentity(MonitorDescriptor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var identity = BuildIdentity(monitor);
        return !string.IsNullOrWhiteSpace(identity.HardwareId)
            || !string.IsNullOrWhiteSpace(identity.DevicePath)
            || !string.IsNullOrWhiteSpace(identity.FriendlyName);
    }

    /// <summary>
    /// Re-enumerates the connected monitors off the UI thread, refreshes the current-target status,
    /// and rebuilds the picker rows (M2-RF-931). Enumeration failures degrade to an empty list with
    /// an explanatory status; the window stays usable (RF-012/RNF-013).
    /// </summary>
    /// <returns>A task that completes when the picker rows are refreshed.</returns>
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            IReadOnlyList<MonitorDescriptor> discovered;
            try
            {
                // Native enumeration is quick but is kept off the dispatcher so a pathological driver
                // can never stall the settings window (RNF-004/RNF-123).
                discovered = await Task.Run(() => _enumerator.Enumerate() ?? []).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Monitor picker: enumerating the connected monitors failed");
                discovered = [];
                TargetStatusText = "The connected monitors could not be enumerated.";
                HasUnavailableTarget = false;
                Monitors = [];
                return;
            }

            BuildRows(discovered);
            UpdateTargetStatus(discovered);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Persists the selection of <paramref name="item"/> as the target monitor identity and applies
    /// the placement without a restart (M2-RF-932). A descriptor with no stable identity is warned
    /// about first (M2-RF-934); failures are surfaced and change nothing (M2-RF-922).
    /// </summary>
    /// <param name="item">The selected monitor; must not be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the selection applied; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is <see langword="null"/>.</exception>
    public async Task<bool> SelectAsync(MonitorPickerItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!item.HasStableIdentity)
        {
            _notifier?.Notify(
                "TouchPanel",
                "The selected monitor exposes no stable identity (no hardware id, device path, or friendly name); "
                + "the target may not be matched reliably.",
                NotificationSeverity.Warning);
        }

        var identity = BuildIdentity(item.Monitor);
        var applied = await ApplyAsync(new SettingsChange
        {
            SetMonitorMatch = true,
            MonitorMatch = identity,
            MonitorFallback = Fallback,
        }).ConfigureAwait(true);

        if (applied)
        {
            await RefreshAsync().ConfigureAwait(true);
        }

        return applied;
    }

    /// <summary>
    /// Applies the fallback policy without changing the target identity (M2-RF-933), preserving the
    /// M1 warning semantics through the shared apply path (the selection pipeline notifies when the
    /// fallback is actually used).
    /// </summary>
    /// <param name="fallback">The requested policy.</param>
    /// <returns><see langword="true"/> when the policy applied; otherwise <see langword="false"/>.</returns>
    public async Task<bool> ApplyFallbackAsync(MonitorFallback fallback)
    {
        var applied = await ApplyAsync(new SettingsChange { MonitorFallback = NormalizeFallback(fallback) })
            .ConfigureAwait(true);
        if (applied)
        {
            Fallback = fallback;
        }

        return applied;
    }

    private async Task<bool> ApplyAsync(SettingsChange change)
    {
        IsBusy = true;
        try
        {
            var result = await _settings.ApplyAsync(change).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                _logger.Warning(
                    "Monitor picker: applying the selection failed. Code={Code}",
                    result.ErrorCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // M2-RF-925/RF-012: a picker failure must never take the window or the panel down.
            _logger.Warning(ex, "Monitor picker: applying the selection failed unexpectedly");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildRows(IReadOnlyList<MonitorDescriptor> discovered)
    {
        var identity = _monitorAccessor()?.Match;
        var rows = new List<MonitorPickerItem>(discovered.Count);

        foreach (var monitor in discovered)
        {
            var isCurrent = identity is not null
                && MonitorMatcher.Match(identity, [monitor]).IsMatch;

            rows.Add(new MonitorPickerItem(
                monitor,
                monitor.DisplayName,
                BuildIdentitySummary(monitor),
                isCurrent,
                monitor.IsPrimary,
                HasStableIdentity(monitor)));
        }

        Monitors = rows;
    }

    private void UpdateTargetStatus(IReadOnlyList<MonitorDescriptor> discovered)
    {
        var identity = _monitorAccessor()?.Match;
        if (identity is null)
        {
            HasUnavailableTarget = false;
            TargetStatusText = "No target monitor has been selected.";
            return;
        }

        var match = MonitorMatcher.Match(identity, discovered);
        if (match.IsMatch)
        {
            HasUnavailableTarget = false;
            TargetStatusText = $"Current target: {match.Monitor!.DisplayName}.";
            return;
        }

        HasUnavailableTarget = true;
        TargetStatusText = $"The configured target monitor is unavailable ({match.Reason}). Select a monitor to update it.";
    }

    private static string BuildIdentitySummary(MonitorDescriptor monitor)
    {
        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(monitor.HardwareIdWithSerial))
        {
            parts.Add(monitor.HardwareIdWithSerial!);
        }

        if (!string.IsNullOrWhiteSpace(monitor.DevicePath))
        {
            parts.Add(monitor.DevicePath!);
        }
        else if (!string.IsNullOrWhiteSpace(monitor.MonitorDeviceId))
        {
            parts.Add(monitor.MonitorDeviceId!);
        }

        parts.Add(monitor.Bounds.IsEmpty
            ? "resolution unknown"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{monitor.Bounds.Width}×{monitor.Bounds.Height} @ ({monitor.Bounds.X},{monitor.Bounds.Y})"));

        if (monitor.IsPrimary)
        {
            parts.Add("primary");
        }

        return string.Join(" · ", parts);
    }

    private static MonitorFallback NormalizeFallback(MonitorFallback fallback) =>
        fallback == MonitorFallback.Primary ? MonitorFallback.Primary : MonitorFallback.None;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
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

    /// <summary>Parameterized asynchronous command carrying the selected <see cref="MonitorPickerItem"/>.</summary>
    private sealed class MonitorAsyncCommand : ICommand
    {
        private readonly Func<MonitorPickerItem, Task> _execute;

        public MonitorAsyncCommand(Func<MonitorPickerItem, Task> execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is MonitorPickerItem;

        public async void Execute(object? parameter)
        {
            if (parameter is not MonitorPickerItem item)
            {
                return;
            }

            try
            {
                await _execute(item).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Never surface an exception to the dispatcher.
            }
        }
    }

    /// <summary>Parameterized asynchronous command carrying the requested <see cref="MonitorFallback"/>.</summary>
    private sealed class FallbackAsyncCommand : ICommand
    {
        private readonly Func<MonitorFallback, Task> _execute;

        public FallbackAsyncCommand(Func<MonitorFallback, Task> execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is MonitorFallback;

        public async void Execute(object? parameter)
        {
            if (parameter is not MonitorFallback fallback)
            {
                return;
            }

            try
            {
                await _execute(fallback).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Never surface an exception to the dispatcher.
            }
        }
    }
}
