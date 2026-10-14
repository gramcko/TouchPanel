using System.Diagnostics;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;

namespace TouchPanel.Services.Logging;

/// <summary>
/// Outcome of <see cref="LoggingSetup.CreateLogger"/>: the logger to use, whether the
/// rolling file sink is active, and why it is not when it is disabled (RF-010).
/// </summary>
/// <param name="Logger">The logger to install as <see cref="Log.Logger"/>.</param>
/// <param name="FileLoggingEnabled">Whether log events are written to rolling files.</param>
/// <param name="FailureReason">The file-sink failure that forced the trace fallback, if any.</param>
public sealed record LoggingInitialization(Logger Logger, bool FileLoggingEnabled, string? FailureReason);

/// <summary>
/// Builds the application logger (DD-1): a rolling file sink bounded to
/// <see cref="LoggingOptions.DefaultRetainedFileCountLimit"/> files of
/// <see cref="LoggingOptions.DefaultFileSizeLimitBytes"/> bytes, with structured
/// compact text output. Initialization never throws: an unwritable log directory
/// degrades to trace output so startup cannot crash or stall (RF-010).
/// </summary>
public static class LoggingSetup
{
    /// <summary>Compact structured output template: timestamp, severity, category, message, exception (RF-008).</summary>
    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    static LoggingSetup()
    {
        // Sink-internal failures (mid-run I/O errors, rotation problems) are surfaced to
        // trace output instead of propagating to the caller (RF-010, RNF-007).
        SelfLog.Enable(message => Trace.WriteLine("[TouchPanel:Serilog] " + message));
    }

    /// <summary>
    /// Creates the application logger. If the rolling file sink cannot be created
    /// (unwritable directory, locked target file, ...), the returned logger writes to
    /// trace output instead and reports the reason; callers must not treat that as fatal.
    /// </summary>
    /// <param name="options">The sink options; <see cref="LoggingOptions.LogDirectory"/> is required.</param>
    /// <returns>The logging initialization result; <see cref="LoggingInitialization.FileLoggingEnabled"/> is false on fallback.</returns>
    public static LoggingInitialization CreateLogger(LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LogDirectory);

        try
        {
            Directory.CreateDirectory(options.LogDirectory);

            var logger = ConfigureFileLogger(options).CreateLogger();
            return new LoggingInitialization(logger, FileLoggingEnabled: true, FailureReason: null);
        }
        catch (Exception ex)
        {
            // Never let logging take the application down (RF-010 / F-19): degrade to
            // trace output and continue; startup must not stall or crash.
            Trace.WriteLine(
                $"TouchPanel: file logging unavailable ({ex.GetType().Name}: {ex.Message}); using trace fallback.");

            var fallback = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Sink(new TraceEventSink())
                .CreateLogger();

            return new LoggingInitialization(fallback, FileLoggingEnabled: false, FailureReason: ex.Message);
        }
    }

    private static LoggerConfiguration ConfigureFileLogger(LoggingOptions options) =>
        new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(options.LogDirectory, options.FileName),
                rollingInterval: options.RollingInterval,
                fileSizeLimitBytes: options.FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: options.RetainedFileCountLimit,
                outputTemplate: OutputTemplate);
}
