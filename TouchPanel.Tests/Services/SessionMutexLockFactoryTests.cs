using TouchPanel.Services.SingleInstance;

namespace TouchPanel.Tests.Services;

/// <summary>
/// Exercises the DD-3 mutex lock factory against real (test-named) session mutexes. No process
/// is launched and the user profile is untouched; test names are unique per test.
/// </summary>
public sealed class SessionMutexLockFactoryTests
{
    [Fact]
    public void TryAcquire_WhenNoOtherOwner_ReturnsLock()
    {
        var factory = new SessionMutexLockFactory(UniqueMutexName());

        using var lockHandle = factory.TryAcquire();

        Assert.NotNull(lockHandle);
    }

    [Fact]
    public void TryAcquire_WhenHeldByAnotherThread_ReturnsNull()
    {
        var factory = new SessionMutexLockFactory(UniqueMutexName());
        using var first = factory.TryAcquire();
        Assert.NotNull(first);

        var second = RunOnOtherThread(factory.TryAcquire);

        Assert.Null(second);
    }

    [Fact]
    public void Dispose_ReleasesOwnership_SoAnotherThreadCanAcquire()
    {
        var factory = new SessionMutexLockFactory(UniqueMutexName());
        var first = factory.TryAcquire();
        Assert.NotNull(first);

        first.Dispose();

        var second = RunOnOtherThread(factory.TryAcquire);
        Assert.NotNull(second);
        second!.Dispose();
    }

    [Fact]
    public void TryAcquire_AfterPreviousOwnerAbandonedTheMutex_ReturnsLock()
    {
        var name = UniqueMutexName();
        using var abandoned = new Mutex(initiallyOwned: false, name);
        var owner = new Thread(() => abandoned.WaitOne());
        owner.Start();
        owner.Join();

        var factory = new SessionMutexLockFactory(name);
        using var lockHandle = factory.TryAcquire();

        Assert.NotNull(lockHandle);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var factory = new SessionMutexLockFactory(UniqueMutexName());
        var lockHandle = factory.TryAcquire();
        Assert.NotNull(lockHandle);

        lockHandle.Dispose();
        var exception = Record.Exception(lockHandle.Dispose);

        Assert.Null(exception);
    }

    private static string UniqueMutexName() => @"Local\TouchPanel.Tests.SingleInstance." + Guid.NewGuid().ToString("N");

    private static T? RunOnOtherThread<T>(Func<T> operation)
    {
        T? result = default;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = operation();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }

        return result;
    }
}
