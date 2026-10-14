using Serilog;
using Serilog.Events;
using TouchPanel.Config;
using TouchPanel.Services.Monitors;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Monitors;

/// <summary>
/// RF-015/RF-016/RF-017 selection behavior with a fake enumerator: resolved target, unavailable
/// with the fallback policy applied (never silent), enumeration failure isolation, and the D-17
/// discovery logging (all monitors once per distinct set, with identity fields).
/// </summary>
public sealed class MonitorSelectionServiceTests
{
    private const string DellModelId = "DEL-A0BC";

    [Fact]
    public void Select_WhenConfiguredMonitorFound_ReturnsAvailableResultAndLogsSelection()
    {
        var dell = Monitor(manufacturer: "DEL", productCode: "A0BC", gdiName: @"\\.\DISPLAY1");
        var (service, sink, _) = Create(dell);

        var result = service.Select(new MonitorConfig
        {
            Match = new MonitorIdentity { HardwareId = DellModelId },
            Fallback = MonitorFallback.None,
        });

        Assert.True(result.IsAvailable);
        Assert.False(result.UsedFallback);
        Assert.True(result.CanShow);
        Assert.Same(dell, result.Monitor);
        Assert.Equal(MonitorFallback.None, result.Fallback);
        Assert.Contains(DellModelId, result.Reason, StringComparison.Ordinal);

        var discovery = Assert.Single(sink.Events, logEvent =>
            logEvent.RenderMessage().Contains("Monitor discovered.", StringComparison.Ordinal));
        Assert.Equal(DellModelId, ScalarProperty(discovery, "HardwareId"));
        Assert.Equal(DellModelId, ScalarProperty(discovery, "HardwareIdWithSerial"));
        Assert.Equal("DEL", ScalarProperty(discovery, "Manufacturer"));
        Assert.Equal("A0BC", ScalarProperty(discovery, "ProductCode"));

        Assert.Contains(sink.Events, logEvent =>
            logEvent.Level == LogEventLevel.Information
            && logEvent.RenderMessage().Contains("target monitor resolved", StringComparison.Ordinal));
    }

    [Fact]
    public void Select_WhenNoMatchAndFallbackNone_ReturnsUnavailableWithReason()
    {
        var acer = Monitor(manufacturer: "ACR", productCode: "1234", gdiName: @"\\.\DISPLAY1");
        var (service, sink, _) = Create(acer);

        var result = service.Select(new MonitorConfig
        {
            Match = new MonitorIdentity { HardwareId = DellModelId },
            Fallback = MonitorFallback.None,
        });

        Assert.False(result.IsAvailable);
        Assert.False(result.UsedFallback);
        Assert.False(result.CanShow);
        Assert.Null(result.Monitor);
        Assert.Equal(MonitorFallback.None, result.Fallback);
        Assert.Contains("hardwareId", result.Reason, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(sink.Events, logEvent =>
            logEvent.Level == LogEventLevel.Warning
            && logEvent.RenderMessage().Contains("unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Select_WhenNoMatchAndFallbackPrimary_ReturnsPrimaryWithFallbackFlag()
    {
        var primary = Monitor(manufacturer: "ACR", productCode: "1234", primary: true, gdiName: @"\\.\DISPLAY1");
        var secondary = Monitor(manufacturer: "ACR", productCode: "5678", gdiName: @"\\.\DISPLAY2");
        var (service, sink, _) = Create(secondary, primary);

        var result = service.Select(new MonitorConfig
        {
            Match = new MonitorIdentity { HardwareId = DellModelId },
            Fallback = MonitorFallback.Primary,
        });

        Assert.False(result.IsAvailable);
        Assert.True(result.UsedFallback);
        Assert.True(result.CanShow);
        Assert.Same(primary, result.Monitor);
        Assert.Equal(MonitorFallback.Primary, result.Fallback);
        Assert.Contains("primary fallback", result.Reason, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(sink.Events, logEvent =>
            logEvent.Level == LogEventLevel.Warning
            && logEvent.RenderMessage().Contains("falling back", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Select_WhenNoMatchAndFallbackPrimaryButNoPrimaryDetected_ReturnsUnavailable()
    {
        var secondary = Monitor(manufacturer: "ACR", productCode: "1234", gdiName: @"\\.\DISPLAY1");
        var (service, _, _) = Create(secondary);

        var result = service.Select(new MonitorConfig
        {
            Match = new MonitorIdentity { HardwareId = DellModelId },
            Fallback = MonitorFallback.Primary,
        });

        Assert.False(result.IsAvailable);
        Assert.False(result.UsedFallback);
        Assert.False(result.CanShow);
        Assert.Null(result.Monitor);
        Assert.Contains("no primary monitor", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_WhenNoMonitorsDetected_ReturnsUnavailableEvenWithPrimaryFallback()
    {
        var (service, _, _) = Create();

        var result = service.Select(new MonitorConfig
        {
            Match = new MonitorIdentity { HardwareId = DellModelId },
            Fallback = MonitorFallback.Primary,
        });

        Assert.False(result.IsAvailable);
        Assert.False(result.CanShow);
        Assert.Null(result.Monitor);
        Assert.Contains("no monitors", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_WhenEnumeratorThrows_IsIsolatedAndReturnsUnavailable()
    {
        var (service, sink, enumerator) = Create();
        enumerator.ExceptionToThrow = new InvalidOperationException("native failure");

        var result = service.Select(new MonitorConfig
        {
            Match = new MonitorIdentity { HardwareId = DellModelId },
            Fallback = MonitorFallback.Primary,
        });

        Assert.False(result.IsAvailable);
        Assert.False(result.CanShow);
        Assert.Null(result.Monitor);
        Assert.Contains(sink.Events, logEvent =>
            logEvent.Level == LogEventLevel.Error
            && logEvent.Exception is InvalidOperationException);
    }

    [Fact]
    public void Select_UnknownFallback_IsTreatedAsNone()
    {
        var acer = Monitor(manufacturer: "ACR", productCode: "1234", primary: true, gdiName: @"\\.\DISPLAY1");
        var (service, _, _) = Create(acer);

        var result = service.Select(new MonitorConfig
        {
            Match = new MonitorIdentity { HardwareId = DellModelId },
            Fallback = MonitorFallback.Unknown,
        });

        Assert.Equal(MonitorFallback.None, result.Fallback);
        Assert.False(result.UsedFallback);
        Assert.Null(result.Monitor);
    }

    [Fact]
    public void Select_FirstRunWithoutCriteria_UsesPrimaryFallback()
    {
        // D-17: first run has no identity criteria and the generated default enables the primary
        // fallback, so the panel targets the primary monitor (reported as a fallback placement).
        var primary = Monitor(primary: true, gdiName: @"\\.\DISPLAY1");
        var secondary = Monitor(gdiName: @"\\.\DISPLAY2");
        var (service, _, _) = Create(secondary, primary);

        var result = service.Select(new MonitorConfig
        {
            Match = null,
            Fallback = MonitorFallback.Primary,
        });

        Assert.False(result.IsAvailable);
        Assert.True(result.UsedFallback);
        Assert.Same(primary, result.Monitor);
        Assert.Contains("no monitor identity criteria", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_WithoutCriteriaAndFallbackNone_ReturnsUnavailable()
    {
        var (service, _, _) = Create(Monitor(primary: true, gdiName: @"\\.\DISPLAY1"));

        var result = service.Select(new MonitorConfig { Match = null, Fallback = MonitorFallback.None });

        Assert.False(result.IsAvailable);
        Assert.False(result.CanShow);
        Assert.Null(result.Monitor);
        Assert.Contains("no monitor identity criteria", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_LogsDiscoveryOncePerDistinctMonitorSet()
    {
        var dell = Monitor(manufacturer: "DEL", productCode: "A0BC", gdiName: @"\\.\DISPLAY1");
        var acer = Monitor(manufacturer: "ACR", productCode: "1234", gdiName: @"\\.\DISPLAY2");
        var (service, sink, enumerator) = Create(dell, acer);

        var config = new MonitorConfig
        {
            Match = new MonitorIdentity { HardwareId = DellModelId },
            Fallback = MonitorFallback.None,
        };

        service.Select(config);
        service.Select(config);

        Assert.Equal(2, DiscoveryCount(sink));
        Assert.Equal(1, SummaryCount(sink));

        // A changed set (monitor connected) is logged again; the previous entries stay in the log.
        enumerator.Monitors.Add(Monitor(manufacturer: "SAM", productCode: "0001", gdiName: @"\\.\DISPLAY3"));
        service.Select(config);

        Assert.Equal(5, DiscoveryCount(sink));
        Assert.Equal(2, SummaryCount(sink));
    }

    [Fact]
    public void Select_ReEvaluatesOnEveryCall_NoPolling()
    {
        var dell = Monitor(manufacturer: "DEL", productCode: "A0BC", gdiName: @"\\.\DISPLAY1");
        var (service, _, enumerator) = Create(dell);

        var config = new MonitorConfig { Match = new MonitorIdentity { HardwareId = DellModelId } };

        var first = service.Select(config);
        var second = service.Select(config);

        Assert.True(first.IsAvailable);
        Assert.True(second.IsAvailable);
        Assert.Equal(2, enumerator.CallCount);

        // The target disappears: the next on-demand evaluation reports unavailable.
        enumerator.Monitors.Clear();
        var third = service.Select(config);
        Assert.False(third.IsAvailable);
        Assert.Equal(3, enumerator.CallCount);
    }

    private static (MonitorSelectionService Service, RecordingSink Sink, FakeMonitorEnumerator Enumerator) Create(
        params MonitorDescriptor[] monitors)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();
        var enumerator = new FakeMonitorEnumerator();
        enumerator.Monitors.AddRange(monitors);

        return (new MonitorSelectionService(enumerator, logger), sink, enumerator);
    }

    private static int DiscoveryCount(RecordingSink sink) =>
        sink.Events.Count(logEvent => logEvent.RenderMessage().Contains("Monitor discovered.", StringComparison.Ordinal));

    private static int SummaryCount(RecordingSink sink) =>
        sink.Events.Count(logEvent => logEvent.RenderMessage().Contains("Monitor discovery:", StringComparison.Ordinal));

    private static object? ScalarProperty(LogEvent logEvent, string propertyName) =>
        logEvent.Properties.TryGetValue(propertyName, out var value) && value is ScalarValue scalar
            ? scalar.Value
            : null;

    private static MonitorDescriptor Monitor(
        string? manufacturer = null,
        string? productCode = null,
        bool primary = false,
        string? gdiName = null) => new()
        {
            Manufacturer = manufacturer,
            ProductCode = productCode,
            IsPrimary = primary,
            Bounds = new PixelRect(0, 0, 1920, 1080),
            WorkArea = new PixelRect(0, 0, 1920, 1080),
            SourceGdiName = gdiName,
        };

    private sealed class FakeMonitorEnumerator : IMonitorEnumerator
    {
        public List<MonitorDescriptor> Monitors { get; } = [];

        public Exception? ExceptionToThrow { get; set; }

        public int CallCount { get; private set; }

        public IReadOnlyList<MonitorDescriptor> Enumerate()
        {
            CallCount++;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Monitors.ToArray();
        }
    }
}
