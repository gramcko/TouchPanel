using Serilog;
using Serilog.Events;
using TouchPanel.Services.Notifications;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Services;

/// <summary>
/// Verifies the RF-010 notification contract: <see cref="TrayNotifier"/> delegates to the
/// injected balloon presenter, falls back to structured logging when the tray is unavailable,
/// and never throws — even when the presenter or the logger fails.
/// </summary>
public sealed class TrayNotifierTests
{
    [Fact]
    public void Notify_WithPresenter_DelegatesTitleMessageAndSeverity()
    {
        var sink = new RecordingSink();
        var logger = CreateLogger(sink);
        string? capturedTitle = null;
        string? capturedMessage = null;
        var capturedSeverity = NotificationSeverity.Info;
        var notifier = new TrayNotifier(logger, (title, message, severity) =>
        {
            capturedTitle = title;
            capturedMessage = message;
            capturedSeverity = severity;
            return true;
        });

        notifier.Notify("Save failed", "Panel could not be saved", NotificationSeverity.Warning);

        Assert.Equal("Save failed", capturedTitle);
        Assert.Equal("Panel could not be saved", capturedMessage);
        Assert.Equal(NotificationSeverity.Warning, capturedSeverity);
    }

    [Fact]
    public void Notify_DefaultSeverity_IsInfo()
    {
        var sink = new RecordingSink();
        var capturedSeverity = NotificationSeverity.Error;
        var notifier = new TrayNotifier(CreateLogger(sink), (_, _, severity) =>
        {
            capturedSeverity = severity;
            return true;
        });

        notifier.Notify("Title", "Message");

        Assert.Equal(NotificationSeverity.Info, capturedSeverity);
    }

    [Fact]
    public void Notify_WhenBalloonShown_DoesNotLogAFallbackWarning()
    {
        var sink = new RecordingSink();
        var notifier = new TrayNotifier(CreateLogger(sink), (_, _, _) => true);

        notifier.Notify("Title", "Message");

        Assert.DoesNotContain(sink.Events, logEvent => logEvent.Level >= LogEventLevel.Warning);
    }

    [Fact]
    public void Notify_WhenTrayUnavailable_LogsFallbackWithContent()
    {
        var sink = new RecordingSink();
        var notifier = new TrayNotifier(CreateLogger(sink), (_, _, _) => false);

        notifier.Notify("Tray failed", "Detail", NotificationSeverity.Error);

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Warning, logEvent.Level);
        Assert.Equal("Tray failed", ScalarProperty(logEvent, "Title"));
        Assert.Equal("Detail", ScalarProperty(logEvent, "Message"));
    }

    [Fact]
    public void Notify_WithoutPresenter_LogsFallback()
    {
        var sink = new RecordingSink();
        var notifier = new TrayNotifier(CreateLogger(sink));

        notifier.Notify("Log-only", "Message");

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Warning, logEvent.Level);
        Assert.Equal("Log-only", ScalarProperty(logEvent, "Title"));
    }

    [Fact]
    public void Notify_WhenPresenterThrows_DoesNotThrowAndLogsFallback()
    {
        var sink = new RecordingSink();
        var notifier = new TrayNotifier(
            CreateLogger(sink),
            (_, _, _) => throw new InvalidOperationException("presenter failed"));

        var exception = Record.Exception(() => notifier.Notify("Title", "Message", NotificationSeverity.Error));

        Assert.Null(exception);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
        Assert.Contains(sink.Events, logEvent => ScalarProperty(logEvent, "Title") == "Title");
    }

    [Fact]
    public void Notify_WhenLoggerThrows_DoesNotThrow()
    {
        var notifier = new TrayNotifier(new ThrowingLogger());

        var exception = Record.Exception(() => notifier.Notify("Title", "Message"));

        Assert.Null(exception);
    }

    private static ILogger CreateLogger(RecordingSink sink) =>
        new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();

    private static string? ScalarProperty(LogEvent logEvent, string propertyName) =>
        logEvent.Properties.TryGetValue(propertyName, out var value) && value is ScalarValue scalar
            ? scalar.Value as string
            : null;

    /// <summary>Logger whose writes always fail, proving the notifier's last-resort guarantee.</summary>
    private sealed class ThrowingLogger : ILogger
    {
        public ILogger ForContext(ILogger sourceContext) => this;

        public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false) => this;

        public bool IsEnabled(LogEventLevel level) => true;

        public void Write(LogEvent logEvent) => throw new InvalidOperationException("logger failed");
    }
}
