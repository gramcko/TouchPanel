using Serilog;

namespace TouchPanel.Services.Logging;

/// <summary>
/// Log entries for the three global exception channels the application subscribes to
/// (RF-011). Kept out of <c>App.xaml.cs</c> (which owns the WPF event wiring) so the
/// logging contract is unit-testable without a WPF application.
/// </summary>
public static class ExceptionLog
{
    /// <summary>Message written for an unhandled exception on the WPF dispatcher.</summary>
    public const string DispatcherMessage = "Unhandled exception on the WPF dispatcher";

    /// <summary>Message written for an unhandled <see cref="AppDomain"/> exception.</summary>
    public const string AppDomainMessage = "Unhandled AppDomain exception";

    /// <summary>Message written for an unobserved task exception.</summary>
    public const string UnobservedTaskMessage = "Unobserved task exception observed";

    /// <summary>Logs a dispatcher exception at fatal severity before it terminates the process.</summary>
    /// <param name="logger">The application logger.</param>
    /// <param name="exception">The exception that escaped the dispatcher.</param>
    public static void DispatcherException(ILogger logger, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        logger.Fatal(exception, DispatcherMessage);
    }

    /// <summary>
    /// Logs an unhandled <see cref="AppDomain"/> exception at fatal severity. The
    /// exception object is not always a managed exception, so it may be <see langword="null"/>.
    /// </summary>
    /// <param name="logger">The application logger.</param>
    /// <param name="exception">The exception payload, when it is a managed exception.</param>
    /// <param name="isTerminating">Whether the runtime is terminating the process.</param>
    public static void AppDomainException(ILogger logger, Exception? exception, bool isTerminating)
    {
        ArgumentNullException.ThrowIfNull(logger);

        logger.Fatal(exception, AppDomainMessage + " (IsTerminating={IsTerminating})", isTerminating);
    }

    /// <summary>Logs an unobserved task exception at error severity; the runtime observes it.</summary>
    /// <param name="logger">The application logger.</param>
    /// <param name="exception">The unobserved exception (typically an <see cref="AggregateException"/>).</param>
    public static void UnobservedTaskException(ILogger logger, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        logger.Error(exception, UnobservedTaskMessage);
    }
}
