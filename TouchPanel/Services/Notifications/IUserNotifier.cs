namespace TouchPanel.Services.Notifications;

/// <summary>
/// Relative urgency of a user-facing notification. The tray implementation maps this to a
/// balloon icon; it is deliberately independent of any native type so callers (configuration,
/// monitoring, startup, tray) do not depend on <c>Interop/</c>.
/// </summary>
public enum NotificationSeverity
{
    /// <summary>Routine, informational message.</summary>
    Info,

    /// <summary>Something degraded or needs attention, but the application keeps working.</summary>
    Warning,

    /// <summary>A failure the user should know about.</summary>
    Error,
}

/// <summary>
/// The RF-010 boundary for user-visible notifications (plan section 1.3). Implementations must
/// never throw and must never block the calling thread (RF-010, RNF-004): a notification that
/// cannot be shown is degraded to structured logging instead of interrupting the UI.
/// </summary>
public interface IUserNotifier
{
    /// <summary>
    /// Shows one notification to the user. Implementations swallow all failures, so this method
    /// never throws and returns immediately; a notification that cannot be shown is logged.
    /// </summary>
    /// <param name="title">Short title of the notification.</param>
    /// <param name="message">Body text of the notification.</param>
    /// <param name="severity">Relative urgency; defaults to <see cref="NotificationSeverity.Info"/>.</param>
    void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info);
}
