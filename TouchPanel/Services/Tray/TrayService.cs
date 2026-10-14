using Serilog;
using TouchPanel.Interop;
using TouchPanel.Services.Notifications;

namespace TouchPanel.Services.Tray;

/// <summary>
/// Owns the tray presence for the running application (RF-005, RF-007, RF-013, RF-024): registers
/// the icon on <see cref="Start"/>, removes it on <see cref="Dispose"/>, retries failed
/// registrations with a bounded backoff driven by the injected <see cref="TimeProvider"/> (DD-14),
/// recovers when the shell broadcasts a taskbar recreation (Explorer restart), and exposes the M1
/// menu as a UI-framework-agnostic model rendered by <c>Views.TrayMenuHost</c>.
/// <para>
/// This class contains no WPF types and no business logic: menu selections are raised as
/// <see cref="CommandSelected"/> for the composition root, and balloons are delegated to the
/// injected <see cref="ITrayIcon"/>. Every path is failure-tolerant (RF-013/RNF-013): no tray
/// operation throws into the caller, and a missing icon never takes the application down.
/// </para>
/// <para>
/// All icon calls are serialized by an internal gate so a retry timer, a <c>TaskbarCreated</c>
/// broadcast, a balloon request from another subsystem, and disposal cannot interleave shell
/// calls or re-add an icon after removal.
/// </para>
/// </summary>
public sealed class TrayService : IDisposable
{
    /// <summary>Default tray tooltip used when <see cref="TrayServiceOptions.Tooltip"/> is not set.</summary>
    public const string DefaultTooltip = "TouchPanel";

    private readonly ITrayIcon _icon;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TrayServiceOptions _options;
    private readonly TrayRegistrationRetryPolicy _retryPolicy;
    private readonly string _tooltip;
    private readonly List<TrayMenuItem> _menuItems;
    private readonly object _gate = new();
    private ITimer? _retryTimer;
    private int _retryAttempt;
    private bool _registered;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayService"/> class and subscribes to the
    /// icon's events so no broadcast is missed between construction and <see cref="Start"/>.
    /// </summary>
    /// <param name="icon">The tray icon seam; production passes a <see cref="TrayIconAdapter"/>.</param>
    /// <param name="logger">The application logger.</param>
    /// <param name="options">Icon and retry configuration; defaults to <see cref="TrayServiceOptions.Default"/>.</param>
    /// <param name="timeProvider">Clock used for retry backoff; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="icon"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    public TrayService(
        ITrayIcon icon,
        ILogger logger,
        TrayServiceOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(logger);

        _icon = icon;
        _logger = logger.ForContext<TrayService>();
        _options = options ?? TrayServiceOptions.Default;
        _retryPolicy = _options.RetryPolicy ?? TrayRegistrationRetryPolicy.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _tooltip = string.IsNullOrWhiteSpace(_options.Tooltip) ? DefaultTooltip : _options.Tooltip;

        _menuItems = new List<TrayMenuItem>
        {
            new(TrayMenuCommand.ShowHidePanel, "Show/Hide Panel"),
            new(TrayMenuCommand.OpenConfigurationFolder, "Open Configuration Folder"),
            new(TrayMenuCommand.Exit, "Exit"),
        };

        _icon.MouseAction += OnIconMouseAction;
        _icon.TaskbarCreated += OnIconTaskbarCreated;
    }

    /// <summary>
    /// Raised when the user requests the tray menu (left or right button released on the icon).
    /// Handlers run on the icon's thread (the WPF UI thread in production); the menu host
    /// marshals when needed and must not activate other windows (RF-024).
    /// </summary>
    public event EventHandler<TrayMenuRequestedEventArgs>? MenuRequested;

    /// <summary>
    /// Raised when a menu item is selected (through <see cref="SelectCommand"/>). The composition
    /// root owns the meaning of each command; the service only forwards it. Handler failures are
    /// isolated and logged so a buggy command cannot break the menu (RNF-013).
    /// </summary>
    public event EventHandler<TrayMenuCommandEventArgs>? CommandSelected;

    /// <summary>Gets a value indicating whether the service believes the icon is registered (RF-013).</summary>
    public bool IsRegistered
    {
        get
        {
            lock (_gate)
            {
                return _registered;
            }
        }
    }

    /// <summary>
    /// Gets the handle of the hidden window that owns the icon, for the menu host's standard tray
    /// foreground pattern (RF-024). 0 while no icon window exists.
    /// </summary>
    public nint WindowHandle => _icon.WindowHandle;

    /// <summary>
    /// Registers the icon with the shell. A failure is logged and retried with the bounded
    /// backoff policy without throwing; repeated calls are ignored. Must be called on the thread
    /// that owns the icon's window (the WPF UI thread).
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _started)
            {
                return;
            }

            _started = true;

            if (TryAddLocked(out var win32Error))
            {
                _registered = true;
                _logger.Information("Tray: icon registered. Tooltip={Tooltip}", _tooltip);
                return;
            }

            _registered = false;
            ScheduleRetryLocked(win32Error);
        }
    }

    /// <summary>
    /// Gets a consistent snapshot of the current menu model. The WPF host reads it every time the
    /// menu opens, so updates made with <see cref="SetMenuItem"/> take effect immediately (the
    /// T-12 seam for the Edit Mode toggle).
    /// </summary>
    /// <returns>The menu items in display order.</returns>
    public IReadOnlyList<TrayMenuItem> GetMenuItems()
    {
        lock (_gate)
        {
            return _menuItems.ToArray();
        }
    }

    /// <summary>
    /// Replaces the menu item with the same command, or appends/inserts it. This is the seam the
    /// T-12 Edit Mode item uses; M1 only builds the three RF-005 items.
    /// </summary>
    /// <param name="item">The item to add or update.</param>
    /// <param name="index">
    /// Position to insert at when the item is not already present; <c>-1</c> (the default)
    /// appends it. An existing item is removed before the insertion, so the index refers to the
    /// resulting list.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> is less than -1.</exception>
    public void SetMenuItem(TrayMenuItem item, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (index < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The index must be -1 (append or replace) or a non-negative position.");
        }

        lock (_gate)
        {
            var existing = _menuItems.FindIndex(candidate => candidate.Command == item.Command);

            if (index < 0)
            {
                if (existing >= 0)
                {
                    _menuItems[existing] = item;
                }
                else
                {
                    _menuItems.Add(item);
                }

                return;
            }

            if (existing >= 0)
            {
                _menuItems.RemoveAt(existing);
            }

            _menuItems.Insert(Math.Min(index, _menuItems.Count), item);
        }
    }

    /// <summary>
    /// Raises <see cref="CommandSelected"/> for a menu selection. Called by the WPF menu host;
    /// never throws when a handler fails.
    /// </summary>
    /// <param name="command">The selected command.</param>
    public void SelectCommand(TrayMenuCommand command)
    {
        _logger.Debug("Tray: menu command selected. Command={Command}", command);

        RaiseCommandSelected(new TrayMenuCommandEventArgs(command));
    }

    /// <summary>
    /// Shows a non-activating balloon through the icon (RF-010, RF-077). Returns
    /// <see langword="false"/> when the tray is unavailable or the shell rejects the balloon, so
    /// <see cref="TrayNotifier"/> can fall back to logging. Never throws and never blocks beyond
    /// the shell call.
    /// </summary>
    /// <param name="title">Short title of the notification.</param>
    /// <param name="message">Body text of the notification.</param>
    /// <param name="severity">Relative urgency, mapped to the balloon icon.</param>
    /// <returns><see langword="true"/> when the balloon was shown.</returns>
    public bool ShowBalloon(string title, string message, NotificationSeverity severity = NotificationSeverity.Info)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                return _icon.ShowBalloon(title ?? string.Empty, message ?? string.Empty, severity);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Tray: showing a balloon failed; the notification falls back to the log");
                return false;
            }
        }
    }

    /// <summary>
    /// Stops retries, unsubscribes from the icon, and removes the icon from the shell (RF-007).
    /// Idempotent and guaranteed not to throw: shutdown must never be blocked by the tray.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _registered = false;

            DisposeRetryTimerLocked();

            _icon.MouseAction -= OnIconMouseAction;
            _icon.TaskbarCreated -= OnIconTaskbarCreated;

            try
            {
                _icon.Delete();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Tray: removing the icon failed");
            }

            try
            {
                _icon.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Tray: disposing the tray icon failed");
            }
        }
    }

    private bool TryAddLocked(out int win32Error)
    {
        try
        {
            var added = _icon.Add(_options.IconHandle, _tooltip);
            win32Error = added ? 0 : SafeLastError();
            return added;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Tray: registering the icon failed unexpectedly");
            win32Error = SafeLastError();
            return false;
        }
    }

    private bool TryReAddLocked(out int win32Error)
    {
        try
        {
            var added = _icon.ReAdd();
            win32Error = added ? 0 : SafeLastError();
            return added;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Tray: re-registering the icon failed unexpectedly");
            win32Error = SafeLastError();
            return false;
        }
    }

    private int SafeLastError()
    {
        try
        {
            return _icon.LastError;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private void ScheduleRetryLocked(int win32Error)
    {
        if (_disposed)
        {
            return;
        }

        if (_retryAttempt >= _retryPolicy.MaxAttempts)
        {
            _logger.Error(
                "Tray: registration failed after {TotalAttempts} attempts; continuing without a tray icon. Re-registration will be retried if Explorer restarts. Win32Error={Win32Error}",
                _retryAttempt + 1,
                win32Error);
            return;
        }

        _retryAttempt++;
        var delay = _retryPolicy.GetDelay(_retryAttempt);

        // Every failure is logged (RF-013) with the scheduled delay and the Win32 error so the
        // log alone explains why the icon is missing.
        _logger.Warning(
            "Tray: registration failed; scheduling retry {Retry} of {MaxRetries}. DelayMs={DelayMs} Win32Error={Win32Error}",
            _retryAttempt,
            _retryPolicy.MaxAttempts,
            (long)delay.TotalMilliseconds,
            win32Error);

        try
        {
            _retryTimer?.Dispose();
            _retryTimer = _timeProvider.CreateTimer(
                static state => ((TrayService)state!).OnRetryTimerElapsed(),
                this,
                delay,
                Timeout.InfiniteTimeSpan);
        }
        catch (Exception ex)
        {
            // A broken timer must not take the application down; the icon stays absent until the
            // next TaskbarCreated broadcast (RF-013).
            _logger.Warning(ex, "Tray: scheduling the registration retry failed; no further retries will be attempted");
        }
    }

    private void OnRetryTimerElapsed()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            DisposeRetryTimerLocked();

            if (TryAddLocked(out var win32Error))
            {
                _registered = true;
                var retries = _retryAttempt;
                _retryAttempt = 0;
                _logger.Information("Tray: icon registered after {Retries} retries. Tooltip={Tooltip}", retries, _tooltip);
                return;
            }

            ScheduleRetryLocked(win32Error);
        }
    }

    private void DisposeRetryTimerLocked()
    {
        var timer = _retryTimer;
        _retryTimer = null;

        if (timer is null)
        {
            return;
        }

        try
        {
            timer.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Tray: disposing the retry timer failed");
        }
    }

    private void OnIconMouseAction(object? sender, TrayIconMouseEventArgs e)
    {
        if (!IsMenuTrigger(e.Message))
        {
            return;
        }

        var handlers = MenuRequested;
        if (handlers is null)
        {
            return;
        }

        var args = new TrayMenuRequestedEventArgs(e.X, e.Y);
        foreach (EventHandler<TrayMenuRequestedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Tray: a MenuRequested handler failed");
            }
        }
    }

    private void OnIconTaskbarCreated(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Explorer restarted and discarded every tray icon. Re-add immediately; if the new
            // shell is not ready yet, restart the bounded retry budget from scratch (RF-013).
            DisposeRetryTimerLocked();
            _retryAttempt = 0;

            _logger.Information("Tray: the shell recreated the taskbar (Explorer restart); re-registering the tray icon");

            if (TryReAddLocked(out var win32Error))
            {
                _registered = true;
                _logger.Information("Tray: icon re-registered. Tooltip={Tooltip}", _tooltip);
                return;
            }

            _registered = false;
            ScheduleRetryLocked(win32Error);
        }
    }

    private void RaiseCommandSelected(TrayMenuCommandEventArgs args)
    {
        var handlers = CommandSelected;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<TrayMenuCommandEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Tray: a CommandSelected handler failed. Command={Command}", args.Command);
            }
        }
    }

    /// <summary>
    /// Determines whether a decoded tray mouse message opens the menu. Both primary button
    /// releases are triggers (the wrapper's documented contract); button-down and double-click
    /// messages are ignored so a double-click does not open the menu twice.
    /// </summary>
    /// <param name="message">The Win32 mouse message from <see cref="TrayIconMouseEventArgs.Message"/>.</param>
    /// <returns><see langword="true"/> when the message is a menu trigger.</returns>
    internal static bool IsMenuTrigger(int message) =>
        message is TrayIcon.WmLButtonUp or TrayIcon.WmRButtonUp;
}
