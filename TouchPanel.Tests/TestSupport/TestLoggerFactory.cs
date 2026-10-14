using Serilog;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Creates an in-memory Serilog logger backed by a <see cref="RecordingSink"/> so tests can assert
/// widget-level logging without touching the file system.
/// </summary>
internal static class TestLoggerFactory
{
    /// <summary>Creates a verbose logger that records every event into the returned sink.</summary>
    /// <returns>The logger and its recording sink.</returns>
    public static (ILogger Logger, RecordingSink Sink) Create()
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (logger, sink);
    }
}
