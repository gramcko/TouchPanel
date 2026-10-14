using System.Diagnostics;
using Serilog;

namespace TouchPanel.Services.Notifications;

/// <summary>
/// Presents one balloon through the tray. Returns <see langword="false"/> when the tray is
/// unavailable (no icon registered or the shell rejected the balloon), which makes
/// <see cref="TrayNotifier"/> fall back to logging. Injected so the notifier is testable without
/// a real tray (T-05 part 2 wires it to <c>Interop.TrayIcon.ShowBalloon</c>).
/// </summary>
/// <param name="title">Short title of the notification.</param>
/// <param name="message">Body text of the notification.</param>
/// <param name="severity">Relative urgency of the notification.</param>
/// <returns><see langword="true"/> when the balloon was accepted by the shell.</returns>
public delegate bool BalloonNotificationPresenter(string title, string message, NotificationSeverity severity);

/// <summary>
/// Default <see cref="IUserNotifier"/> (plan section 1.3): delegates to an injectable balloon
/// presenter (the tray icon, in production) and falls back to structured logging whenever the
/// tray is unavailable or the presenter fails. Guarantees of this class (RF-010, RNF-004):
/// <list type="bullet">
/// <item><description><see cref="Notify"/> never throws, even when the presenter or the logger does.</description></item>
/// <item><description><see cref="Notify"/> never blocks: it performs no I/O beyond the shell call and log write.</description></item>
/// </list>
/// </summary>
public sealed class TrayNotifier : IUserNotifier
{
    private readonly ILogger _logger;
    private readonly BalloonNotificationPresenter? _presentBalloon;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayNotifier"/> class.
    /// </summary>
    /// <param name="logger">The application logger used for the fallback path.</param>
    /// <param name="presentBalloon">
    /// The balloon presenter (typically <c>trayIcon.ShowBalloon</c>); when <see langword="null"/>
    /// the notifier is log-only, which is the correct behavior before the tray exists.
    /// </param>
    public TrayNotifier(ILogger logger, BalloonNotificationPresenter? presentBalloon = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger.ForContext<TrayNotifier>();
        _presentBalloon = presentBalloon;
    }

    /// <inheritdoc />
    public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info)
    {
        try
        {
            NotifyCore(title ?? string.Empty, message ?? string.Empty, severity);
        }
        catch (Exception ex)
        {
            // Last-resort diagnostics: the notification contract must never propagate a failure
            // into configuration, monitoring, startup, or tray code (RF-010, RNF-013).
            Trace.WriteLine($"TouchPanel: user notification failed ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private void NotifyCore(string title, string message, NotificationSeverity severity)
    {
        var shown = false;

        if (_presentBalloon is not null)
        {
            try
            {
                shown = _presentBalloon(title, message, severity);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "User notification: showing the balloon failed; falling back to the log");
            }
        }

        if (shown)
        {
            _logger.Debug("User notification shown. Severity={Severity} Title={Title}", severity, title);
            return;
        }

        _logger.Warning(
            "User notification not shown as a tray balloon (tray unavailable); logged instead. Severity={Severity} Title={Title} Message={Message}",
            severity,
            title,
            message);
    }
}
