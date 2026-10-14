using Serilog;
using Serilog.Events;
using TouchPanel.Services.Logging;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Services;

public class ExceptionLogTests
{
    private static (ILogger Logger, RecordingSink Sink) Create()
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (logger, sink);
    }

    [Fact]
    public void DispatcherException_LogsFatalWithExceptionData()
    {
        var (logger, sink) = Create();
        var exception = new InvalidOperationException("ui boom");

        ExceptionLog.DispatcherException(logger, exception);

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Fatal, logEvent.Level);
        Assert.Same(exception, logEvent.Exception);
        Assert.Contains(ExceptionLog.DispatcherMessage, logEvent.RenderMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AppDomainException_LogsFatalWithTerminatingFlag()
    {
        var (logger, sink) = Create();
        var exception = new OutOfMemoryException("terminating");

        ExceptionLog.AppDomainException(logger, exception, isTerminating: true);

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Fatal, logEvent.Level);
        Assert.Same(exception, logEvent.Exception);
        var message = logEvent.RenderMessage();
        Assert.Contains(ExceptionLog.AppDomainMessage, message, StringComparison.Ordinal);
        Assert.Contains("True", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppDomainException_WithoutManagedException_StillLogs()
    {
        var (logger, sink) = Create();

        var thrown = Record.Exception(() => ExceptionLog.AppDomainException(logger, exception: null, isTerminating: false));

        Assert.Null(thrown);
        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Fatal, logEvent.Level);
        Assert.Null(logEvent.Exception);
    }

    [Fact]
    public void UnobservedTaskException_LogsErrorWithExceptionData()
    {
        var (logger, sink) = Create();
        var exception = new AggregateException(new TimeoutException("task failed"));

        ExceptionLog.UnobservedTaskException(logger, exception);

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Error, logEvent.Level);
        Assert.Same(exception, logEvent.Exception);
        Assert.Contains(ExceptionLog.UnobservedTaskMessage, logEvent.RenderMessage(), StringComparison.Ordinal);
    }
}
