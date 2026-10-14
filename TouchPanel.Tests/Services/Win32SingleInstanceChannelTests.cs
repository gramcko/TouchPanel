using TouchPanel.Interop;
using TouchPanel.Services.SingleInstance;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Services;

/// <summary>
/// Exercises the DD-3 channel in-process (no second process): a listening primary window,
/// signaling to it, the no-primary case, and disposal. Window work runs on a dedicated STA thread.
/// </summary>
public sealed class Win32SingleInstanceChannelTests
{
    [Fact]
    public async Task TrySignalRunningInstance_AfterStartListening_DeliversShowRequest()
    {
        using var sta = new StaThread();
        var channel = new Win32SingleInstanceChannel(UniqueWindowName());
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await sta.InvokeAsync(() => channel.StartListening(() => received.TrySetResult(), () => { }));

            Assert.True(channel.TrySignalRunningInstance());

            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await sta.InvokeAsync(channel.Dispose);
        }
    }

    [Fact]
    public async Task TrySignalExit_AfterStartListening_DeliversExitRequest()
    {
        using var sta = new StaThread();
        var channel = new Win32SingleInstanceChannel(UniqueWindowName());
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await sta.InvokeAsync(() => channel.StartListening(() => { }, () => received.TrySetResult()));

            Assert.True(channel.TrySignalExit());

            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await sta.InvokeAsync(channel.Dispose);
        }
    }

    [Fact]
    public async Task StartListening_ShowAndExitRequests_AreDeliveredToTheirOwnCallbacks()
    {
        using var sta = new StaThread();
        var channel = new Win32SingleInstanceChannel(UniqueWindowName());
        var showReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exitReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await sta.InvokeAsync(() => channel.StartListening(
                () => showReceived.TrySetResult(),
                () => exitReceived.TrySetResult()));

            Assert.True(channel.TrySignalRunningInstance());
            Assert.True(channel.TrySignalExit());

            await showReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await exitReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await sta.InvokeAsync(channel.Dispose);
        }
    }

    [Fact]
    public void TrySignalExit_WithoutPrimary_ReturnsFalse()
    {
        using var channel = new Win32SingleInstanceChannel(UniqueWindowName());

        Assert.False(channel.TrySignalExit());
    }

    [Fact]
    public void TrySignalRunningInstance_WithoutPrimary_ReturnsFalse()
    {
        using var channel = new Win32SingleInstanceChannel(UniqueWindowName());

        Assert.False(channel.TrySignalRunningInstance());
    }

    [Fact]
    public async Task Dispose_StopsListening_SoTheWindowDisappearsAndSignalsFail()
    {
        using var sta = new StaThread();
        var name = UniqueWindowName();
        var channel = new Win32SingleInstanceChannel(name);

        await sta.InvokeAsync(() => channel.StartListening(() => { }, () => { }));
        Assert.NotEqual(nint.Zero, MessageWindow.FindMessageWindow(name));

        await sta.InvokeAsync(channel.Dispose);

        Assert.Equal(nint.Zero, MessageWindow.FindMessageWindow(name));
        Assert.False(channel.TrySignalRunningInstance());
    }

    [Fact]
    public async Task StartListening_SecondCall_IsIgnoredAndKeepsTheFirstCallback()
    {
        using var sta = new StaThread();
        var channel = new Win32SingleInstanceChannel(UniqueWindowName());
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await sta.InvokeAsync(() =>
            {
                channel.StartListening(() => first.TrySetResult(), () => { });
                channel.StartListening(
                    () => throw new InvalidOperationException("second callback must not replace the first"),
                    () => throw new InvalidOperationException("second callback must not replace the first"));
            });

            Assert.True(channel.TrySignalRunningInstance());
            await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await sta.InvokeAsync(channel.Dispose);
        }
    }

    private static string UniqueWindowName() => "TouchPanel.Tests.SignalChannel." + Guid.NewGuid().ToString("N");
}
