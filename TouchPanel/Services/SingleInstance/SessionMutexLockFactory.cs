namespace TouchPanel.Services.SingleInstance;

/// <summary>
/// <see cref="ISingleInstanceLockFactory"/> backed by a session-scoped named mutex
/// (<c>Local\TouchPanel.SingleInstance</c>, DD-3). The primary instance owns the mutex for the
/// lifetime of the process; any other process fails to acquire it and becomes the secondary.
/// </summary>
public sealed class SessionMutexLockFactory : ISingleInstanceLockFactory
{
    private readonly string _mutexName;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionMutexLockFactory"/> class.
    /// </summary>
    /// <param name="mutexName">The session-scoped mutex name; defaults to the name fixed by DD-3.</param>
    public SessionMutexLockFactory(string mutexName = SingleInstanceService.MutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        _mutexName = mutexName;
    }

    /// <inheritdoc />
    public ISingleInstanceLock? TryAcquire()
    {
        // Create without initial ownership, then try to take ownership explicitly so
        // "an object already exists" never conflates with "another process owns it".
        var mutex = new Mutex(initiallyOwned: false, _mutexName, out _);
        try
        {
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // The previous primary terminated without releasing; ownership transfers to us,
                // so this process is still the only running instance.
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }

            return new HeldMutexLock(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    /// <summary>Releases the named mutex when the owning service is disposed.</summary>
    private sealed class HeldMutexLock : ISingleInstanceLock
    {
        private Mutex? _mutex;

        internal HeldMutexLock(Mutex mutex)
        {
            _mutex = mutex;
        }

        public void Dispose()
        {
            var mutex = Interlocked.Exchange(ref _mutex, null);
            if (mutex is null)
            {
                return;
            }

            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The thread no longer owns the mutex (already released or abandoned); the
                // handle still has to be closed, so this must not escape.
            }

            mutex.Dispose();
        }
    }
}
