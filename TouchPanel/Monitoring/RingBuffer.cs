namespace TouchPanel.Monitoring;

/// <summary>
/// A fixed-capacity circular buffer (RF-069/M2-RF-691..692). The storage never grows: appends are
/// O(1) and allocate nothing once the buffer is constructed, and once full the oldest sample is
/// overwritten by the newest. Reads return a consistent oldest-first copy taken under the same
/// lock that serializes writes, so a reader on the UI thread can never observe a torn or
/// partially written sample while the polling thread appends.
/// </summary>
/// <remarks>
/// <para><b>Threading (M2-RF-692).</b> Writes and reads are serialized by a private lock; the copy
/// returned by <see cref="ToArray"/> is independent of later writes. A single polling thread writes
/// and one or more UI readers copy; the lock is held only for the duration of the copy.</para>
/// <para><b>Bounded memory (M2-RF-694).</b> The backing array has exactly <see cref="Capacity"/>
/// elements for the instance's lifetime (except an explicit <see cref="Resize"/>), so the retained
/// memory is <c>Capacity × sizeof(T)</c> plus a small constant.</para>
/// </remarks>
/// <typeparam name="T">The element type; <see cref="SensorHistoryStore"/> uses <c>double?</c> so an unavailable sample is a gap, not a zero.</typeparam>
public sealed class RingBuffer<T>
{
    private readonly object _gate = new();
    private T[] _items;
    private int _start;
    private int _count;

    /// <summary>
    /// Initializes a new instance of the <see cref="RingBuffer{T}"/> class with a fixed capacity.
    /// </summary>
    /// <param name="capacity">The maximum number of samples retained; must be at least one.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is not positive.</exception>
    public RingBuffer(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The ring buffer capacity must be at least one.");
        }

        _items = new T[capacity];
    }

    /// <summary>Gets the fixed number of samples this buffer can retain.</summary>
    public int Capacity
    {
        get
        {
            lock (_gate)
            {
                return _items.Length;
            }
        }
    }

    /// <summary>Gets the number of samples currently retained (0..<see cref="Capacity"/>).</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>Gets a value indicating whether the buffer holds no samples.</summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _count == 0;
            }
        }
    }

    /// <summary>
    /// Appends one sample (RF-067). When the buffer is full the oldest sample is overwritten, so
    /// the buffer always retains the newest <see cref="Capacity"/> samples in insertion order.
    /// </summary>
    /// <param name="item">The sample to append.</param>
    public void Add(T item)
    {
        lock (_gate)
        {
            if (_count < _items.Length)
            {
                _items[(_start + _count) % _items.Length] = item;
                _count++;
            }
            else
            {
                // Full: overwrite the oldest slot and advance the start.
                _items[_start] = item;
                _start = (_start + 1) % _items.Length;
            }
        }
    }

    /// <summary>
    /// Returns a consistent copy of the retained samples ordered oldest → newest. The copy is taken
    /// under the write lock, so it is never torn; later appends do not mutate the returned array.
    /// </summary>
    /// <returns>A new array with <see cref="Count"/> entries in insertion order.</returns>
    public T[] ToArray()
    {
        lock (_gate)
        {
            var result = new T[_count];
            for (var i = 0; i < _count; i++)
            {
                result[i] = _items[(_start + i) % _items.Length];
            }

            return result;
        }
    }

    /// <summary>Removes every retained sample and resets the read position.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_items);
            _start = 0;
            _count = 0;
        }
    }

    /// <summary>
    /// Changes the capacity while preserving as much existing history as still fits
    /// (RF-069/M2-RF-695): when shrinking, the oldest samples are dropped; when growing, all
    /// retained samples are kept. Used when the polling interval changes.
    /// </summary>
    /// <param name="capacity">The new capacity; must be at least one.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is not positive.</exception>
    public void Resize(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The ring buffer capacity must be at least one.");
        }

        lock (_gate)
        {
            if (capacity == _items.Length)
            {
                return;
            }

            var kept = Math.Min(_count, capacity);
            var dropped = _count - kept;
            var replaced = new T[capacity];
            for (var i = 0; i < kept; i++)
            {
                replaced[i] = _items[(_start + dropped + i) % _items.Length];
            }

            _items = replaced;
            _start = 0;
            _count = kept;
        }
    }
}
