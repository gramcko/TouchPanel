using Serilog;
using Serilog.Events;
using TouchPanel.Services.Display;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Services;

/// <summary>
/// T-08 part 2 (RF-022): the display-change watcher is driven entirely by the injected source
/// seam — tests raise events directly, with no OS involvement. Covers the marshalling contract,
/// burst coalescing, failure isolation, and disposal ownership.
/// </summary>
public sealed class DisplayChangeWatcherTests
{
    [Fact]
    public void SourceChanged_IsMarshalledThroughPost_AndRaisesOnce()
    {
        var source = new FakeDisplayChangeSource();
        Action? posted = null;
        var watcher = new DisplayChangeWatcher(source, CreateLogger(out _), action => posted = action);
        var raised = 0;
        watcher.DisplayChanged += (_, _) => raised++;

        source.Raise();

        // The subscriber must not run on the source's thread: nothing happened yet.
        Assert.NotNull(posted);
        Assert.Equal(0, raised);

        posted!();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void SourceChanged_WithoutMarshaller_RaisesSynchronously()
    {
        var source = new FakeDisplayChangeSource();
        var watcher = new DisplayChangeWatcher(source, CreateLogger(out _));
        var raised = 0;
        watcher.DisplayChanged += (_, _) => raised++;

        source.Raise();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void SourceChanged_BurstBeforeThePostRuns_IsCoalescedIntoASingleRaise()
    {
        var source = new FakeDisplayChangeSource();
        var posted = new List<Action>();
        var watcher = new DisplayChangeWatcher(source, CreateLogger(out _), posted.Add);
        var raised = 0;
        watcher.DisplayChanged += (_, _) => raised++;

        source.Raise();
        source.Raise();
        source.Raise();

        var scheduled = Assert.Single(posted);
        scheduled();
        Assert.Equal(1, raised);

        // After the burst was drained, the next notification is scheduled again.
        source.Raise();
        Assert.Equal(2, posted.Count);
        posted[^1]();
        Assert.Equal(2, raised);
    }

    [Fact]
    public void SubscriberThrows_IsIsolatedAndLogged()
    {
        var source = new FakeDisplayChangeSource();
        var logger = CreateLogger(out var sink);
        var watcher = new DisplayChangeWatcher(source, logger);
        watcher.DisplayChanged += (_, _) => throw new InvalidOperationException("subscriber failed");

        var exception = Record.Exception(source.Raise);

        Assert.Null(exception);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Error);
    }

    [Fact]
    public void PostThrows_IsIsolatedAndLogged_AndTheNextEventIsStillScheduled()
    {
        var source = new FakeDisplayChangeSource();
        var logger = CreateLogger(out var sink);
        var attempts = 0;
        var watcher = new DisplayChangeWatcher(source, logger, _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("dispatcher unavailable");
            }
        });

        Assert.Null(Record.Exception(source.Raise));
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);

        // The failed scheduling must release the coalescing gate.
        Assert.Null(Record.Exception(source.Raise));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Dispose_UnsubscribesAndDisposesTheSource()
    {
        var source = new FakeDisplayChangeSource();
        var watcher = new DisplayChangeWatcher(source, CreateLogger(out _));
        var raised = 0;
        watcher.DisplayChanged += (_, _) => raised++;

        Assert.Equal(1, source.SubscriberCount);

        watcher.Dispose();

        Assert.Equal(0, source.SubscriberCount);
        Assert.Equal(1, source.DisposeCount);

        // Events raised after disposal must be ignored; disposing twice must be safe.
        source.Raise();
        Assert.Equal(0, raised);
        Assert.Null(Record.Exception(watcher.Dispose));
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public void Dispose_WhenSourceUnsubscribeOrDisposeThrows_DoesNotThrow()
    {
        var source = new FakeDisplayChangeSource { ThrowOnRemove = true, ThrowOnDispose = true };
        var logger = CreateLogger(out var sink);
        var watcher = new DisplayChangeWatcher(source, logger);

        var exception = Record.Exception(watcher.Dispose);

        Assert.Null(exception);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
    }

    private static ILogger CreateLogger(out RecordingSink sink)
    {
        sink = new RecordingSink();
        return new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
    }

    /// <summary>Test double for the OS notification: tests raise <see cref="Changed"/> explicitly.</summary>
    private sealed class FakeDisplayChangeSource : IDisplayChangeSource
    {
        private EventHandler? _changed;

        public event EventHandler? Changed
        {
            add
            {
                if (ThrowOnAdd)
                {
                    throw new InvalidOperationException("subscribe failed");
                }

                _changed += value;
            }

            remove
            {
                if (ThrowOnRemove)
                {
                    throw new InvalidOperationException("unsubscribe failed");
                }

                _changed -= value;
            }
        }

        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

        public int DisposeCount { get; private set; }

        public bool ThrowOnAdd { get; set; }

        public bool ThrowOnRemove { get; set; }

        public bool ThrowOnDispose { get; set; }

        public void Raise() => _changed?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("dispose failed");
            }
        }
    }
}
