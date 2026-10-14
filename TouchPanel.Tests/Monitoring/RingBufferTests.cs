using TouchPanel.Monitoring;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Bounded ring-buffer behavior (T-03, RF-069/M2-RF-691..692): fixed capacity, oldest-first
/// ordering, wrap-at-capacity, consistent reads, resize-preserving-newest, and safety under
/// concurrent writes and reads.
/// </summary>
public sealed class RingBufferTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveCapacity(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(capacity));
    }

    [Fact]
    public void Add_BelowCapacity_PreservesInsertionOrder()
    {
        var buffer = new RingBuffer<int>(4);

        buffer.Add(10);
        buffer.Add(20);
        buffer.Add(30);

        Assert.Equal(4, buffer.Capacity);
        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { 10, 20, 30 }, buffer.ToArray());
    }

    [Fact]
    public void Add_AtCapacity_WrapsAndKeepsTheNewestSamplesInOrder()
    {
        var buffer = new RingBuffer<int>(3);

        for (var i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal(3, buffer.Capacity);

        // Oldest two (1, 2) were overwritten; newest three remain oldest → newest.
        Assert.Equal(new[] { 3, 4, 5 }, buffer.ToArray());
    }

    [Fact]
    public void ToArray_ReturnsAnIndependentConsistentCopy_NotAffectedByLaterWrites()
    {
        var buffer = new RingBuffer<int>(4);
        buffer.Add(1);
        buffer.Add(2);

        var first = buffer.ToArray();
        buffer.Add(3);

        Assert.Equal(new[] { 1, 2 }, first);
        Assert.Equal(new[] { 1, 2, 3 }, buffer.ToArray());
    }

    [Fact]
    public void Clear_RemovesEverythingAndResetsOrdering()
    {
        var buffer = new RingBuffer<int>(2);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        buffer.Clear();

        Assert.True(buffer.IsEmpty);
        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.ToArray());

        buffer.Add(9);
        Assert.Equal(new[] { 9 }, buffer.ToArray());
    }

    [Fact]
    public void Resize_Shrinking_PreservesTheNewestSamples()
    {
        var buffer = new RingBuffer<int>(5);
        for (var i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        buffer.Resize(2);

        Assert.Equal(2, buffer.Capacity);
        Assert.Equal(new[] { 4, 5 }, buffer.ToArray());
    }

    [Fact]
    public void Resize_Growing_PreservesAllExistingSamples()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        buffer.Resize(6);

        Assert.Equal(6, buffer.Capacity);
        Assert.Equal(new[] { 1, 2, 3 }, buffer.ToArray());

        // The extra capacity is usable and wraps correctly.
        buffer.Add(4);
        Assert.Equal(new[] { 1, 2, 3, 4 }, buffer.ToArray());
    }

    [Fact]
    public void Resize_RejectsNonPositiveCapacity()
    {
        var buffer = new RingBuffer<int>(2);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Resize(0));
    }

    [Fact]
    public async Task ConcurrentWritesAndReads_NeverObserveTornOrReorderedSamples()
    {
        const int capacity = 64;
        const int total = 20_000;
        var buffer = new RingBuffer<int>(capacity);
        var failures = 0;

        var writer = Task.Run(() =>
        {
            for (var i = 1; i <= total; i++)
            {
                buffer.Add(i);
            }
        });

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            var deadline = Environment.TickCount64 + 2_000;
            while (!writer.IsCompleted && Environment.TickCount64 < deadline)
            {
                var snapshot = buffer.ToArray();
                if (snapshot.Length > capacity)
                {
                    Interlocked.Increment(ref failures);
                    return;
                }

                // A consistent copy is always a contiguous ascending run (each append adds 1).
                for (var i = 1; i < snapshot.Length; i++)
                {
                    if (snapshot[i] != snapshot[i - 1] + 1)
                    {
                        Interlocked.Increment(ref failures);
                        return;
                    }
                }
            }
        })).ToArray();

        await Task.WhenAll(readers.Append(writer));

        Assert.Equal(0, Volatile.Read(ref failures));
        Assert.Equal(capacity, buffer.Count);

        var final = buffer.ToArray();
        Assert.Equal(Enumerable.Range(total - capacity + 1, capacity), final);
    }
}
