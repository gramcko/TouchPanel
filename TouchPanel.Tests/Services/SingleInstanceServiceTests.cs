using Serilog;
using Serilog.Events;
using TouchPanel.Services.SingleInstance;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Services;

/// <summary>
/// RF-001/RF-002 decision logic with fakes for the OS seam: primary, secondary, signaling
/// failure, fail-open, event forwarding, and disposal.
/// </summary>
public sealed class SingleInstanceServiceTests
{
    [Fact]
    public void ContractNames_MatchApprovedDecision()
    {
        Assert.Equal(@"Local\TouchPanel.SingleInstance", SingleInstanceService.MutexName);
        Assert.Equal("TouchPanel.ShowPanel.v1", SingleInstanceService.ShowPanelMessageName);
        Assert.Equal("TouchPanel.Exit.v1", SingleInstanceService.ExitMessageName);
    }

    [Fact]
    public void Start_WhenLockAcquired_IsPrimaryAndStartsListening()
    {
        var (service, locks, channel, sink) = StartService(acquiredLock: new FakeLock());

        using (service)
        {
            Assert.True(service.IsPrimary);
            Assert.False(service.SignaledRunningInstance);
            Assert.Equal(1, locks.AcquireCount);
            Assert.Equal(1, channel.StartListeningCount);
            Assert.Equal(0, channel.SignalCount);
            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Information
                    && e.RenderMessage().Contains("primary", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Start_WhenLockHeld_SignalsRunningInstanceAndIsSecondary()
    {
        var (service, _, channel, sink) = StartService(acquiredLock: null);

        using (service)
        {
            Assert.False(service.IsPrimary);
            Assert.True(service.SignaledRunningInstance);
            Assert.Equal(1, channel.SignalCount);
            Assert.Equal(0, channel.StartListeningCount);
            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Information
                    && e.RenderMessage().Contains("requested it to show the panel", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Start_WhenLockHeldAndSignalFails_LogsWarningAndIsNotPrimary()
    {
        var (service, _, channel, sink) = StartService(acquiredLock: null, signalResult: false);

        using (service)
        {
            Assert.False(service.IsPrimary);
            Assert.False(service.SignaledRunningInstance);
            Assert.Equal(1, channel.SignalCount);
            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Warning
                    && e.RenderMessage().Contains("could not be signaled", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Start_WhenSignalThrows_LogsWarningAndIsNotPrimary()
    {
        var failure = new InvalidOperationException("post failed");
        var (service, _, channel, sink) = StartService(acquiredLock: null, signalFailure: failure);

        using (service)
        {
            Assert.False(service.IsPrimary);
            Assert.False(service.SignaledRunningInstance);
            Assert.Equal(1, channel.SignalCount);
            var warning = Assert.Single(
                sink.Events,
                e => e.Level == LogEventLevel.Warning && ReferenceEquals(e.Exception, failure));
            Assert.Contains("signaling the running instance failed", warning.RenderMessage(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Start_WhenLockFactoryThrows_FailsOpenAsPrimary()
    {
        var failure = new UnauthorizedAccessException("mutex denied");
        var (service, _, channel, sink) = StartService(lockFailure: failure);

        using (service)
        {
            Assert.True(service.IsPrimary);
            Assert.Equal(1, channel.StartListeningCount);
            var warning = Assert.Single(
                sink.Events,
                e => e.Level == LogEventLevel.Warning && ReferenceEquals(e.Exception, failure));
            Assert.Contains("continuing as primary", warning.RenderMessage(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Start_WhenListeningThrows_StaysPrimaryAndLogsWarning()
    {
        var failure = new InvalidOperationException("window creation failed");
        var (service, _, channel, sink) = StartService(acquiredLock: new FakeLock(), listenFailure: failure);

        using (service)
        {
            Assert.True(service.IsPrimary);
            Assert.Equal(1, channel.StartListeningCount);
            var warning = Assert.Single(
                sink.Events,
                e => e.Level == LogEventLevel.Warning && ReferenceEquals(e.Exception, failure));
            Assert.Contains("listen", warning.RenderMessage(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ShowPanelRequested_WhenRequestReceived_RaisesEventAndLogs()
    {
        var (service, _, channel, sink) = StartService(acquiredLock: new FakeLock());
        var raised = 0;

        using (service)
        {
            service.ShowPanelRequested += (_, _) => raised++;

            channel.RequestShowPanel();
            channel.RequestShowPanel();

            Assert.Equal(2, raised);
            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Information
                    && e.RenderMessage().Contains("requested the panel to be shown", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ShowPanelRequested_WhenHandlerThrows_IsolatesTheFailureAndKeepsOtherHandlers()
    {
        var (service, _, channel, sink) = StartService(acquiredLock: new FakeLock());
        var secondHandlerCalled = false;

        using (service)
        {
            service.ShowPanelRequested += (_, _) => throw new InvalidOperationException("handler bug");
            service.ShowPanelRequested += (_, _) => secondHandlerCalled = true;

            var exception = Record.Exception(channel.RequestShowPanel);

            Assert.Null(exception);
            Assert.True(secondHandlerCalled);
            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Warning
                    && e.RenderMessage().Contains("handler failed", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Start_WithExitIntent_WhenLockHeld_SignalsExitAndIsSecondary()
    {
        var (service, _, channel, sink) = StartService(acquiredLock: null, intent: LaunchIntent.Exit);

        using (service)
        {
            Assert.False(service.IsPrimary);
            Assert.True(service.SignaledExit);
            Assert.False(service.SignaledRunningInstance);
            Assert.Equal(1, channel.ExitSignalCount);
            Assert.Equal(0, channel.SignalCount);
            Assert.Equal(0, channel.StartListeningCount);
            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Information
                    && e.RenderMessage().Contains("requested it to exit", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Start_WithExitIntent_WhenLockHeldAndSignalFails_LogsWarningAndIsNotPrimary()
    {
        var (service, _, channel, sink) = StartService(acquiredLock: null, signalResult: false, intent: LaunchIntent.Exit);

        using (service)
        {
            Assert.False(service.IsPrimary);
            Assert.False(service.SignaledExit);
            Assert.Equal(1, channel.ExitSignalCount);
            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Warning
                    && e.RenderMessage().Contains("could not be signaled to exit", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Start_WithExitIntent_WhenLockAcquired_IsPrimaryAndStartsListening()
    {
        var (service, _, channel, _) = StartService(acquiredLock: new FakeLock(), intent: LaunchIntent.Exit);

        using (service)
        {
            Assert.True(service.IsPrimary);
            Assert.False(service.SignaledExit);
            Assert.Equal(1, channel.StartListeningCount);
            Assert.Equal(0, channel.ExitSignalCount);
        }
    }

    [Fact]
    public void ExitRequested_WhenRequestReceived_RaisesEventAndLogs()
    {
        var (service, _, channel, sink) = StartService(acquiredLock: new FakeLock());
        var raised = 0;

        using (service)
        {
            service.ExitRequested += (_, _) => raised++;

            channel.RequestExit();
            channel.RequestExit();

            Assert.Equal(2, raised);
            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Information
                    && e.RenderMessage().Contains("requested this instance to exit", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ExitRequested_WhenHandlerThrows_IsolatesTheFailureAndKeepsOtherHandlers()
    {
        var (service, _, channel, sink) = StartService(acquiredLock: new FakeLock());
        var secondHandlerCalled = false;

        using (service)
        {
            service.ExitRequested += (_, _) => throw new InvalidOperationException("handler bug");
            service.ExitRequested += (_, _) => secondHandlerCalled = true;

            var exception = Record.Exception(channel.RequestExit);

            Assert.Null(exception);
            Assert.True(secondHandlerCalled);
            Assert.Contains(
                sink.Events,
                e => e.Level == LogEventLevel.Warning
                    && e.RenderMessage().Contains("ExitRequested handler failed", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Dispose_DisposesChannelAndLock_AndIsIdempotent()
    {
        var lockHandle = new FakeLock();
        var (service, _, channel, _) = StartService(acquiredLock: lockHandle);

        service.Dispose();
        service.Dispose();

        Assert.Equal(1, channel.DisposeCount);
        Assert.Equal(1, lockHandle.DisposeCount);
    }

    [Fact]
    public void Dispose_SecondaryService_StillDisposesChannel()
    {
        var (service, _, channel, _) = StartService(acquiredLock: null);

        service.Dispose();

        Assert.Equal(1, channel.DisposeCount);
    }

    [Fact]
    public void Start_NullArguments_Throw()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var locks = new FakeLockFactory();
        var channel = new FakeChannel();

        Assert.Throws<ArgumentNullException>(() => SingleInstanceService.Start(null!, locks, channel));
        Assert.Throws<ArgumentNullException>(() => SingleInstanceService.Start(logger, null!, channel));
        Assert.Throws<ArgumentNullException>(() => SingleInstanceService.Start(logger, locks, null!));
    }

    private static (SingleInstanceService Service, FakeLockFactory Locks, FakeChannel Channel, RecordingSink Sink) StartService(
        ISingleInstanceLock? acquiredLock = null,
        Exception? lockFailure = null,
        bool signalResult = true,
        Exception? signalFailure = null,
        Exception? listenFailure = null,
        LaunchIntent intent = LaunchIntent.ShowPanel)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
        var locks = new FakeLockFactory { Result = acquiredLock, Failure = lockFailure };
        var channel = new FakeChannel
        {
            SignalResult = signalResult,
            SignalFailure = signalFailure,
            StartListeningFailure = listenFailure,
        };

        var service = SingleInstanceService.Start(logger, locks, channel, intent);
        return (service, locks, channel, sink);
    }

    private sealed class FakeLock : ISingleInstanceLock
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeLockFactory : ISingleInstanceLockFactory
    {
        public ISingleInstanceLock? Result { get; set; }

        public Exception? Failure { get; set; }

        public int AcquireCount { get; private set; }

        public ISingleInstanceLock? TryAcquire()
        {
            AcquireCount++;

            if (Failure is not null)
            {
                throw Failure;
            }

            return Result;
        }
    }

    private sealed class FakeChannel : ISingleInstanceChannel
    {
        public Action? ShowPanelCallback { get; private set; }

        public Action? ExitCallback { get; private set; }

        public int StartListeningCount { get; private set; }

        public int SignalCount { get; private set; }

        public int ExitSignalCount { get; private set; }

        public bool SignalResult { get; set; } = true;

        public Exception? SignalFailure { get; set; }

        public Exception? StartListeningFailure { get; set; }

        public int DisposeCount { get; private set; }

        public void StartListening(Action onShowPanelRequested, Action onExitRequested)
        {
            StartListeningCount++;

            if (StartListeningFailure is not null)
            {
                throw StartListeningFailure;
            }

            ShowPanelCallback = onShowPanelRequested;
            ExitCallback = onExitRequested;
        }

        public bool TrySignalRunningInstance()
        {
            SignalCount++;

            if (SignalFailure is not null)
            {
                throw SignalFailure;
            }

            return SignalResult;
        }

        public bool TrySignalExit()
        {
            ExitSignalCount++;

            if (SignalFailure is not null)
            {
                throw SignalFailure;
            }

            return SignalResult;
        }

        /// <summary>Simulates the primary receiving a show-panel request from a later launch.</summary>
        public void RequestShowPanel() => ShowPanelCallback?.Invoke();

        /// <summary>Simulates the primary receiving an exit request from a later launch.</summary>
        public void RequestExit() => ExitCallback?.Invoke();

        public void Dispose() => DisposeCount++;
    }
}
