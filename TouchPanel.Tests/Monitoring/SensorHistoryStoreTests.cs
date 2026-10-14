using TouchPanel.Monitoring;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Bounded telemetry history (T-03, RF-069/M2-RF-691..697): capacity math and clamp, lazy tracked
/// set with release/reconcile, oldest-first bounded slices, explicit unavailable gaps, capacity
/// recompute preserving newest history, disposal, concurrency, and feeding from the monitoring loop.
/// </summary>
public sealed class SensorHistoryStoreTests
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    [Theory]
    [InlineData(60, 1, 60)]
    [InlineData(10, 3, 4)]           // ceil(10/3) = 4
    [InlineData(10, 5, 2)]           // ceil(2) = 2 (minimum)
    [InlineData(1, 1, 2)]            // below minimum → clamped up
    public void ComputeCapacity_CeilsWindowOverInterval_AndClamps(double windowSeconds, double intervalSeconds, int expected)
    {
        Assert.Equal(expected, SensorHistoryStore.ComputeCapacity(windowSeconds, TimeSpan.FromSeconds(intervalSeconds)));
    }

    [Fact]
    public void ComputeCapacity_ClampsToTheDocumentedMaximum()
    {
        // 3600 s at 0.5 s = 7200 samples, clamped to 3600 (M2-RF-691).
        Assert.Equal(SensorHistoryStore.MaximumCapacity, SensorHistoryStore.ComputeCapacity(3600, TimeSpan.FromMilliseconds(500)));
        Assert.Equal(SensorHistoryStore.MaximumCapacity, SensorHistoryStore.ComputeCapacity(double.MaxValue, TimeSpan.FromMilliseconds(1)));
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(60, 0)]
    [InlineData(60, -1)]
    public void ComputeCapacity_InvalidInputs_ReturnTheMinimum(double windowSeconds, double intervalSeconds)
    {
        Assert.Equal(SensorHistoryStore.MinimumCapacity, SensorHistoryStore.ComputeCapacity(windowSeconds, TimeSpan.FromSeconds(intervalSeconds)));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SensorHistoryStore(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SensorHistoryStore(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Track_CreatesBuffersLazily_AndIsCaseInsensitive()
    {
        using var store = new SensorHistoryStore(OneSecond);

        Assert.Equal(0, store.TrackedCount);
        Assert.True(store.Track("cpu.total"));
        Assert.False(store.Track("CPU.TOTAL"));
        Assert.True(store.IsTracked("  Cpu.Total  "));
        Assert.Equal(1, store.TrackedCount);
    }

    [Fact]
    public void Track_RejectsBlankKeys()
    {
        using var store = new SensorHistoryStore(OneSecond);

        Assert.Throws<ArgumentException>(() => store.Track("   "));
    }

    [Fact]
    public void Release_RemovesTheBufferAndItsHistory()
    {
        using var store = new SensorHistoryStore(OneSecond);
        store.Track("cpu.total");
        store.Record(new SensorSnapshot { CpuTotalPercent = 42 });

        Assert.True(store.Release("cpu.total"));
        Assert.False(store.Release("cpu.total"));
        Assert.False(store.IsTracked("cpu.total"));
        Assert.Empty(store.GetHistory("cpu.total"));
        Assert.Equal(0, store.GetCount("cpu.total"));
    }

    [Fact]
    public void SetTracked_ReconcilesAddedAndReleasedKeys()
    {
        using var store = new SensorHistoryStore(OneSecond);
        store.Track("ram.percent");

        store.SetTracked(["cpu.total", "gpu.usage"]);

        Assert.True(store.IsTracked("cpu.total"));
        Assert.True(store.IsTracked("gpu.usage"));
        Assert.False(store.IsTracked("ram.percent"));
        Assert.Equal(2, store.TrackedCount);
    }

    [Fact]
    public void Record_StoresOnlyTrackedMetrics_AndIgnoresUntrackedOnes()
    {
        using var store = new SensorHistoryStore(OneSecond);
        store.Track("cpu.total");

        store.Record(new SensorSnapshot { CpuTotalPercent = 12.5, GpuUsagePercent = 80 });

        Assert.Equal(new double?[] { 12.5 }, store.GetHistory("cpu.total"));
        Assert.Empty(store.GetHistory("gpu.usage"));
    }

    [Fact]
    public void Record_UnavailableSample_IsStoredAsAnExplicitNullGapNeverZero()
    {
        using var store = new SensorHistoryStore(OneSecond);
        store.Track("temp.cpu");

        store.Record(new SensorSnapshot { CpuTemperatureCelsius = 55 });
        store.Record(new SensorSnapshot { CpuTemperatureCelsius = null });
        store.Record(new SensorSnapshot { CpuTemperatureCelsius = 60 });

        Assert.Equal(new double?[] { 55, null, 60 }, store.GetHistory("temp.cpu"));
    }

    [Fact]
    public void Record_NetworkThroughput_MovesAndRecordsGapsForUnavailableSamples()
    {
        using var store = new SensorHistoryStore(OneSecond);
        store.Track(SensorKeys.NetworkDown);

        store.Record(new SensorSnapshot { NetworkDownBytesPerSecond = 0 });
        store.Record(new SensorSnapshot { NetworkDownBytesPerSecond = 1048576 });
        store.Record(new SensorSnapshot { NetworkDownBytesPerSecond = null });

        Assert.Equal(new double?[] { 0, 1048576, null }, store.GetHistory(SensorKeys.NetworkDown));
    }

    [Fact]
    public void GetHistory_UntrackedMetric_ReturnsEmpty()
    {
        using var store = new SensorHistoryStore(OneSecond);

        Assert.Empty(store.GetHistory("cpu.total"));
        Assert.Equal(0, store.GetCount("cpu.total"));
    }

    [Fact]
    public void Record_WrapsAtCapacity_KeepingNewestSamplesOldestFirst()
    {
        using var store = new SensorHistoryStore(OneSecond);
        store.Track("cpu.total");
        var capacity = store.Capacity;

        for (var i = 1; i <= capacity + 2; i++)
        {
            store.Record(new SensorSnapshot { CpuTotalPercent = i });
        }

        var history = store.GetHistory("cpu.total");
        Assert.Equal(capacity, history.Count);
        Assert.Equal(Enumerable.Range(3, capacity).Select(i => (double?)i), history);
    }

    [Fact]
    public void Reconfigure_RecomputesCapacity_AndPreservesTheNewestHistory()
    {
        // Window 10 s, interval 1 s → capacity 10.
        using var store = new SensorHistoryStore(OneSecond, windowSeconds: 10);
        store.Track("cpu.total");
        for (var i = 1; i <= 10; i++)
        {
            store.Record(new SensorSnapshot { CpuTotalPercent = i });
        }

        // Shrink the window to 5 s → capacity 5, keeping the newest five (6..10).
        store.Reconfigure(windowSeconds: 5, interval: OneSecond);

        Assert.Equal(5, store.Capacity);
        Assert.Equal(new double?[] { 6, 7, 8, 9, 10 }, store.GetHistory("cpu.total"));

        // Grow again to 10 s → capacity 10, existing history preserved.
        store.Reconfigure(windowSeconds: 10, interval: OneSecond);

        Assert.Equal(10, store.Capacity);
        Assert.Equal(new double?[] { 6, 7, 8, 9, 10 }, store.GetHistory("cpu.total"));
    }

    [Fact]
    public void Reconfigure_Interval_ReflectsConstructorAndReconfigure()
    {
        using var store = new SensorHistoryStore(OneSecond, windowSeconds: 10);
        Assert.Equal(OneSecond, store.Interval);

        var twoSeconds = TimeSpan.FromSeconds(2);
        store.Reconfigure(windowSeconds: 10, interval: twoSeconds);

        Assert.Equal(twoSeconds, store.Interval);
        Assert.Equal(5, store.Capacity);
    }

    [Fact]
    public void Reconfigure_GrowingTheWindow_PreservesTheNewestSamples()
    {
        // Window 60 s at 1 s → capacity 60; a 300 s configuration grows it to 300 (M2-RF-691).
        using var store = new SensorHistoryStore(OneSecond, windowSeconds: 60);
        store.Track("cpu.total");
        for (var i = 1; i <= 60; i++)
        {
            store.Record(new SensorSnapshot { CpuTotalPercent = i });
        }

        store.Reconfigure(windowSeconds: 300, interval: OneSecond);

        Assert.Equal(300, store.Capacity);
        Assert.Equal(60, store.GetCount("cpu.total"));
        Assert.Equal(1, store.GetHistory("cpu.total")[0]);
        Assert.Equal(60, store.GetHistory("cpu.total")[^1]);
    }

    [Fact]
    public void EstimateMemoryBytes_IsBoundedByCapacityTimesTrackedMetrics()
    {
        Assert.Equal(
            (long)SensorHistoryStore.MaximumCapacity * 8 * SensorHistoryStore.EstimatedBytesPerSample,
            SensorHistoryStore.EstimateMemoryBytes(SensorHistoryStore.MaximumCapacity, 8));

        Assert.Throws<ArgumentOutOfRangeException>(() => SensorHistoryStore.EstimateMemoryBytes(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SensorHistoryStore.EstimateMemoryBytes(1, -1));
    }

    [Fact]
    public void Clear_EmptiesBuffersButKeepsTracking()
    {
        using var store = new SensorHistoryStore(OneSecond);
        store.Track("cpu.total");
        store.Record(new SensorSnapshot { CpuTotalPercent = 5 });

        store.Clear();

        Assert.True(store.IsTracked("cpu.total"));
        Assert.Empty(store.GetHistory("cpu.total"));
    }

    [Fact]
    public void Dispose_ClearsBuffers_AndRejectsFurtherWrites()
    {
        var store = new SensorHistoryStore(OneSecond);
        store.Track("cpu.total");
        store.Record(new SensorSnapshot { CpuTotalPercent = 5 });

        store.Dispose();

        // RF-697: no buffer or sample reference survives disposal.
        Assert.Equal(0, store.TrackedCount);
        Assert.Empty(store.GetHistory("cpu.total"));
        Assert.Throws<ObjectDisposedException>(() => store.Track("gpu.usage"));
        Assert.Throws<ObjectDisposedException>(() => store.Record(new SensorSnapshot { CpuTotalPercent = 1 }));
        Assert.Throws<ObjectDisposedException>(() => store.Reconfigure(60, OneSecond));

        // Disposal is idempotent.
        store.Dispose();
    }

    [Fact]
    public async Task ConcurrentRecordAndRead_StayConsistentAndBounded()
    {
        using var store = new SensorHistoryStore(OneSecond, windowSeconds: 10);
        store.Track("cpu.total");
        var failures = 0;

        var writer = Task.Run(() =>
        {
            for (var i = 1; i <= 50_000; i++)
            {
                store.Record(new SensorSnapshot { CpuTotalPercent = i });
            }
        });

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            var deadline = Environment.TickCount64 + 2_000;
            while (!writer.IsCompleted && Environment.TickCount64 < deadline)
            {
                var history = store.GetHistory("cpu.total");
                if (history.Count > store.Capacity)
                {
                    Interlocked.Increment(ref failures);
                    return;
                }

                for (var i = 1; i < history.Count; i++)
                {
                    if (history[i] is not { } current || history[i - 1] is not { } previous || current != previous + 1)
                    {
                        Interlocked.Increment(ref failures);
                        return;
                    }
                }
            }
        })).ToArray();

        await Task.WhenAll(readers.Append(writer));

        Assert.Equal(0, Volatile.Read(ref failures));
        Assert.Equal(store.Capacity, store.GetCount("cpu.total"));
    }

    [Fact]
    public void MonitoringService_FeedsEveryProducedSnapshotIntoTheHistoryStore()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        using var store = new SensorHistoryStore(OneSecond);
        store.Track("cpu.total");
        var source = new FakeSensorSource
        {
            SnapshotFactory = poll => new SensorSnapshot { CpuTotalPercent = poll },
        };
        using var service = new MonitoringService(source, clock, logger: logger, history: store);

        service.PublishOnce();
        service.PublishOnce();
        service.PublishOnce();

        Assert.Equal(new double?[] { 1, 2, 3 }, store.GetHistory("cpu.total"));
    }

    [Fact]
    public void MonitoringService_UnavailableSnapshot_IsRecordedAsAGap()
    {
        var clock = new FakeTimeProvider();
        var (logger, _) = TestLoggerFactory.Create();
        using var store = new SensorHistoryStore(OneSecond);
        store.Track("cpu.total");
        var source = new FakeSensorSource(); // default: unavailable snapshot
        using var service = new MonitoringService(source, clock, logger: logger, history: store);

        service.PublishOnce();

        Assert.Equal(new double?[] { null }, store.GetHistory("cpu.total"));
    }
}
