namespace TouchPanel.Config;

/// <summary>Severity of a configuration load diagnostic, used for logging and notifications.</summary>
public enum ConfigDiagnosticSeverity
{
    /// <summary>Informational context (for example, a missing optional property defaulted).</summary>
    Info,

    /// <summary>Entry-level problem that was contained (RF-079): skip, clamp, or disable with warning.</summary>
    Warning,

    /// <summary>File-level problem (RF-075.3/.4) that triggered the recovery path.</summary>
    Error,
}

/// <summary>
/// One diagnostic produced while loading, migrating, or validating a configuration. Diagnostics
/// carry enough context to be logged or surfaced through a notification without extra lookups.
/// </summary>
/// <param name="Severity">How serious the finding is.</param>
/// <param name="Code">Stable machine-readable code (for example <c>config.missing</c>).</param>
/// <param name="Message">Human-readable description for logs and notifications.</param>
public sealed record ConfigDiagnostic(ConfigDiagnosticSeverity Severity, string Code, string Message)
{
    /// <summary>Creates an <see cref="ConfigDiagnosticSeverity.Info"/> diagnostic.</summary>
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <returns>The diagnostic.</returns>
    public static ConfigDiagnostic Info(string code, string message) =>
        new(ConfigDiagnosticSeverity.Info, code, message);

    /// <summary>Creates a <see cref="ConfigDiagnosticSeverity.Warning"/> diagnostic.</summary>
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <returns>The diagnostic.</returns>
    public static ConfigDiagnostic Warning(string code, string message) =>
        new(ConfigDiagnosticSeverity.Warning, code, message);

    /// <summary>Creates a <see cref="ConfigDiagnosticSeverity.Error"/> diagnostic.</summary>
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <returns>The diagnostic.</returns>
    public static ConfigDiagnostic Error(string code, string message) =>
        new(ConfigDiagnosticSeverity.Error, code, message);
}
