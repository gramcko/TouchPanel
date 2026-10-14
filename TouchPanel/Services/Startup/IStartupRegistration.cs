namespace TouchPanel.Services.Startup;

/// <summary>
/// Testable seam over the single per-user startup registry value
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TouchPanel</c>, DD-4/RF-003). The
/// production implementation uses <c>Microsoft.Win32.Registry</c>; unit tests inject an
/// in-memory fake so no test ever writes to the real user hive (RNF-006).
/// </summary>
/// <remarks>
/// Implementations may throw for genuine failures (for example access denied); the
/// <see cref="RegistryStartupRegistration"/> service above this seam catches, logs, and reports
/// them without letting them reach the caller (RF-004).
/// </remarks>
public interface IStartupRegistry
{
    /// <summary>Reads the current value of the startup entry.</summary>
    /// <returns>The exact registered command, or <see langword="null"/> when the value does not exist.</returns>
    string? ReadCommand();

    /// <summary>Creates or updates the startup entry.</summary>
    /// <param name="command">The command line to register.</param>
    void WriteCommand(string command);

    /// <summary>Removes the startup entry; a value that is already absent is not an error.</summary>
    void RemoveCommand();
}

/// <summary>
/// Immutable view of the per-user startup entry at one moment (RF-003/RF-004): whether it
/// exists, the exact registered command, and whether that command matches what this build would
/// register for the current executable.
/// </summary>
public sealed record StartupRegistrationSnapshot
{
    /// <summary>Gets a value indicating whether the entry could be read.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Gets a value indicating whether a startup value exists.</summary>
    public required bool Exists { get; init; }

    /// <summary>
    /// Gets a value indicating whether the registered command points at the expected current
    /// executable. A missing, blank, differently-cased-on-a-case-insensitive-path, unquoted, or
    /// differently-targeted command is not current (RF-004).
    /// </summary>
    public required bool IsCurrent { get; init; }

    /// <summary>Gets the exact registered command, or <see langword="null"/> when it is missing or unreadable.</summary>
    public string? Command { get; init; }

    /// <summary>
    /// Gets a log- and notification-safe description of the read failure, when
    /// <see cref="Succeeded"/> is <see langword="false"/>.
    /// </summary>
    public string? FailureMessage { get; init; }
}

/// <summary>Action taken (or not taken) by a startup-registration apply operation.</summary>
public enum StartupRegistrationAction
{
    /// <summary>The entry already matched the configured state or the operation failed; nothing changed.</summary>
    None,

    /// <summary>The entry was created or updated to point at the current executable.</summary>
    Registered,

    /// <summary>The entry was removed.</summary>
    Removed,
}

/// <summary>
/// Outcome of applying the configured start-with-Windows intent. Failures carry a
/// notification-safe message (never raw exception text) so the caller can surface them through
/// <see cref="Notifications.IUserNotifier"/> (RF-004).
/// </summary>
public sealed record StartupRegistrationResult
{
    /// <summary>Gets a value indicating whether the desired state was applied.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Gets what the operation changed, if anything.</summary>
    public required StartupRegistrationAction Action { get; init; }

    /// <summary>Gets a stable machine-readable failure code; <see langword="null"/> on success.</summary>
    public string? FailureCode { get; init; }

    /// <summary>Gets the human-readable, notification-safe message; <see langword="null"/> on success.</summary>
    public string? Message { get; init; }

    /// <summary>Creates a successful no-change result.</summary>
    /// <returns>The result.</returns>
    public static StartupRegistrationResult NoChange() =>
        new() { Succeeded = true, Action = StartupRegistrationAction.None };

    /// <summary>Creates a successful registration result.</summary>
    /// <returns>The result.</returns>
    public static StartupRegistrationResult Registered() =>
        new() { Succeeded = true, Action = StartupRegistrationAction.Registered };

    /// <summary>Creates a successful removal result.</summary>
    /// <returns>The result.</returns>
    public static StartupRegistrationResult Removed() =>
        new() { Succeeded = true, Action = StartupRegistrationAction.Removed };

    /// <summary>Creates a failed result with a stable code and a notification-safe message.</summary>
    /// <param name="code">Stable machine-readable failure code.</param>
    /// <param name="message">Human-readable failure description (already safe to log and notify).</param>
    /// <returns>The result.</returns>
    public static StartupRegistrationResult Failure(string code, string message) =>
        new()
        {
            Succeeded = false,
            Action = StartupRegistrationAction.None,
            FailureCode = code,
            Message = message,
        };
}

/// <summary>
/// The RF-003/RF-004 boundary for the per-user, no-elevation start-with-Windows registration
/// (plan section 1.3, DD-11). Implementations never throw for read/write/remove failures: every
/// failure is caught, logged with a redaction-safe message, and returned as an unsuccessful
/// result so the composition root can notify the user and keep running (RF-004, RF-086).
/// </summary>
public interface IStartupRegistration
{
    /// <summary>
    /// Reads the current registration without modifying it.
    /// </summary>
    /// <returns>
    /// A snapshot of the entry; on failure <see cref="StartupRegistrationSnapshot.Succeeded"/>
    /// is <see langword="false"/>, the failure is logged, and the method returns normally.
    /// </returns>
    StartupRegistrationSnapshot Read();

    /// <summary>
    /// Applies the configured intent, self-healing the registration per RF-004:
    /// <list type="bullet">
    /// <item><description><paramref name="startWithWindows"/> <see langword="true"/> and the entry is missing or stale: logs a warning and re-registers the quoted current executable path.</description></item>
    /// <item><description><paramref name="startWithWindows"/> <see langword="false"/> and the entry exists: removes it.</description></item>
    /// <item><description>Anything else: no change.</description></item>
    /// </list>
    /// </summary>
    /// <param name="startWithWindows">The configured desired state (<c>startup.startWithWindows</c>).</param>
    /// <returns>The outcome; failures carry a notification-safe message. Never throws.</returns>
    StartupRegistrationResult Apply(bool startWithWindows);
}
