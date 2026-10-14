using Serilog;

namespace TouchPanel.Services.Logging;

/// <summary>
/// Canonical startup and shutdown log entries (RF-007, RF-008). Constants are exposed
/// so tests and diagnostics can assert the entries were written.
/// </summary>
public static class AppLog
{
    /// <summary>Message fragment written on every startup.</summary>
    public const string StartupMessage = "TouchPanel starting";

    /// <summary>Message fragment written on every graceful shutdown.</summary>
    public const string ShutdownMessage = "TouchPanel shutting down";

    /// <summary>Writes the startup entry with the runtime context needed for diagnostics.</summary>
    /// <param name="logger">The application logger.</param>
    /// <param name="logDirectory">The resolved log directory (so the entry is self-locating).</param>
    public static void Startup(ILogger logger, string logDirectory)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var version = typeof(AppLog).Assembly.GetName().Version?.ToString() ?? "unknown";
        logger.Information(
            StartupMessage + ". Version={Version} OS={OSVersion} Runtime={RuntimeVersion} LogDirectory={LogDirectory}",
            version,
            Environment.OSVersion.VersionString,
            Environment.Version.ToString(),
            logDirectory);
    }

    /// <summary>Writes the shutdown entry (RF-007).</summary>
    /// <param name="logger">The application logger.</param>
    public static void Shutdown(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        logger.Information(ShutdownMessage);
    }
}
