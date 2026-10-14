using Serilog;

namespace TouchPanel.Services.SingleInstance;

/// <summary>
/// RF-001/RF-002 single-instance decision logic (DD-3). The first process acquires the
/// session-scoped lock and listens for show-panel requests from later launches; later processes
/// signal the primary and report <see cref="IsPrimary"/> = <see langword="false"/> so the
/// composition root can exit without creating a window. Every failure here is logged and
/// non-fatal (RNF-013). All OS interaction goes through the injected
/// <see cref="ISingleInstanceLockFactory"/> and <see cref="ISingleInstanceChannel"/> seams, so
/// this class is unit-testable without real processes or windows.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    /// <summary>Session-scoped mutex name fixed by DD-3.</summary>
    public const string MutexName = @"Local\TouchPanel.SingleInstance";

    /// <summary>Registered window message used to ask the running instance to show the panel (DD-3).</summary>
    public const string ShowPanelMessageName = "TouchPanel.ShowPanel.v1";

    /// <summary>Registered window message used to ask the running instance to exit (RF-006).</summary>
    public const string ExitMessageName = "TouchPanel.Exit.v1";

    /// <summary>Title of the hidden window that receives the show-panel request.</summary>
    public const string SignalWindowName = "TouchPanel.SingleInstance.SignalWindow.v1";

    private readonly ILogger _logger;
    private readonly ISingleInstanceChannel _channel;
    private readonly LaunchIntent _intent;
    private ISingleInstanceLock? _lock;
    private bool _disposed;

    private SingleInstanceService(ILogger logger, ISingleInstanceChannel channel, LaunchIntent intent)
    {
        _logger = logger;
        _channel = channel;
        _intent = intent;
    }

    /// <summary>Gets a value indicating whether this process owns the session lock (RF-001).</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>Gets a value indicating whether a running primary instance accepted the show-panel request.</summary>
    public bool SignaledRunningInstance { get; private set; }

    /// <summary>Gets a value indicating whether a running primary instance accepted the exit request.</summary>
    public bool SignaledExit { get; private set; }

    /// <summary>
    /// Raised on the channel's thread when a later launch requests the panel to be shown
    /// (RF-002). The T-08 window subscribes; in the meantime the request is still logged.
    /// </summary>
    public event EventHandler? ShowPanelRequested;

    /// <summary>
    /// Raised on the channel's thread when a later launch requests this instance to exit
    /// (RF-006). The composition root subscribes and runs the same ordered shutdown as the tray
    /// Exit command.
    /// </summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// Acquires the session lock and either starts listening (primary) or signals the running
    /// instance (secondary). Never throws for OS failures: the application keeps working in a
    /// degraded mode rather than failing to start (RNF-013).
    /// </summary>
    /// <param name="logger">The application logger.</param>
    /// <param name="lockFactory">Factory for the session-scoped lock.</param>
    /// <param name="channel">The primary/secondary signaling channel.</param>
    /// <param name="intent">What this launch asks for; defaults to the historical show-panel behavior.</param>
    /// <returns>The started service; inspect <see cref="IsPrimary"/> to decide whether to continue.</returns>
    public static SingleInstanceService Start(
        ILogger logger,
        ISingleInstanceLockFactory lockFactory,
        ISingleInstanceChannel channel,
        LaunchIntent intent = LaunchIntent.ShowPanel)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(lockFactory);
        ArgumentNullException.ThrowIfNull(channel);

        var service = new SingleInstanceService(logger.ForContext<SingleInstanceService>(), channel, intent);
        service.Initialize(lockFactory);
        return service;
    }

    /// <summary>Releases the listening window and the session lock. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _channel.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Single instance: disposing the signaling channel failed");
        }

        try
        {
            _lock?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Single instance: releasing the session lock failed");
        }

        _lock = null;
    }

    private void Initialize(ISingleInstanceLockFactory lockFactory)
    {
        var lockFactoryFailed = false;
        try
        {
            _lock = lockFactory.TryAcquire();
        }
        catch (Exception ex)
        {
            // Fail open: refusing to start because the mutex could not be created would be worse
            // for a personal utility than running without enforcement; the degradation is logged.
            lockFactoryFailed = true;
            _logger.Warning(
                ex,
                "Single instance: the session lock could not be created; continuing as primary without single-instance enforcement. Mutex={MutexName}",
                MutexName);
        }

        if (_lock is null && !lockFactoryFailed)
        {
            SignalRunningInstance();
            return;
        }

        IsPrimary = true;
        StartListening();
    }

    private void SignalRunningInstance()
    {
        if (_intent == LaunchIntent.Exit)
        {
            SignalRunningInstanceExit();
            return;
        }

        try
        {
            SignaledRunningInstance = _channel.TrySignalRunningInstance();
        }
        catch (Exception ex)
        {
            SignaledRunningInstance = false;
            _logger.Warning(ex, "Single instance: signaling the running instance failed");
        }

        if (SignaledRunningInstance)
        {
            _logger.Information(
                "Single instance: another instance is running; requested it to show the panel. Message={MessageName}",
                ShowPanelMessageName);
        }
        else
        {
            _logger.Warning(
                "Single instance: another instance holds the session lock but could not be signaled; exiting without creating a panel. Window={WindowName}",
                SignalWindowName);
        }
    }

    private void SignalRunningInstanceExit()
    {
        try
        {
            SignaledExit = _channel.TrySignalExit();
        }
        catch (Exception ex)
        {
            SignaledExit = false;
            _logger.Warning(ex, "Single instance: signaling the running instance to exit failed");
        }

        if (SignaledExit)
        {
            _logger.Information(
                "Single instance: another instance is running; requested it to exit. Message={MessageName}",
                ExitMessageName);
        }
        else
        {
            _logger.Warning(
                "Single instance: another instance holds the session lock but could not be signaled to exit; exiting. Window={WindowName}",
                SignalWindowName);
        }
    }

    private void StartListening()
    {
        try
        {
            _channel.StartListening(HandleShowPanelRequested, HandleExitRequested);
            _logger.Information(
                "Single instance: primary instance started; listening for second-launch requests. Mutex={MutexName}",
                MutexName);
        }
        catch (Exception ex)
        {
            // Without the listener a second launch cannot reach this instance, but the panel
            // itself is unaffected, so this stays a warning.
            _logger.Warning(
                ex,
                "Single instance: failed to listen for second-launch requests; continuing without signaling support. Window={WindowName}",
                SignalWindowName);
        }
    }

    private void HandleShowPanelRequested()
    {
        _logger.Information("Single instance: a second launch requested the panel to be shown");
        RaiseEvent(ShowPanelRequested, "Single instance: a ShowPanelRequested handler failed");
    }

    private void HandleExitRequested()
    {
        _logger.Information("Single instance: a launch requested this instance to exit");
        RaiseEvent(ExitRequested, "Single instance: an ExitRequested handler failed");
    }

    private void RaiseEvent(EventHandler? handlers, string failureMessage)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // One misbehaving subscriber must not prevent the others or reach the message pump.
                _logger.Warning(ex, failureMessage);
            }
        }
    }
}
