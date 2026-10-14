using TouchPanel.Interop;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Interop;

/// <summary>
/// Exercises <see cref="MessageWindow"/> against real user32 windows in-process: lookup by
/// unique name, message delivery, the <c>TaskbarCreated</c> hook, and destruction on dispose.
/// Window work runs on a dedicated STA thread; no other process is involved.
/// </summary>
public sealed class MessageWindowTests
{
    [Fact]
    public async Task MessageOnlyWindow_IsFoundByItsUniqueName()
    {
        using var sta = new StaThread();
        var name = UniqueWindowName();
        var window = await sta.InvokeAsync(() => new MessageWindow(name));

        try
        {
            Assert.True(window.MessageOnly);
            Assert.NotEqual(nint.Zero, window.Handle);
            Assert.Equal(window.Handle, await sta.InvokeAsync(() => MessageWindow.FindMessageWindow(name)));
        }
        finally
        {
            await sta.InvokeAsync(window.Dispose);
        }
    }

    [Fact]
    public async Task HiddenTopLevelWindow_IsFoundByItsUniqueName()
    {
        using var sta = new StaThread();
        var name = UniqueWindowName();
        var window = await sta.InvokeAsync(() => new MessageWindow(name, messageOnly: false));

        try
        {
            Assert.False(window.MessageOnly);
            Assert.Equal(
                window.Handle,
                await sta.InvokeAsync(() => MessageWindow.FindMessageWindow(name, messageOnly: false)));
        }
        finally
        {
            await sta.InvokeAsync(window.Dispose);
        }
    }

    [Fact]
    public async Task PostedRegisteredMessage_RaisesMessageReceivedWithParameters()
    {
        using var sta = new StaThread();
        var name = UniqueWindowName();
        var window = await sta.InvokeAsync(() => new MessageWindow(name));

        try
        {
            const uint wParam = 0x1234;
            const uint lParam = 0x5678;
            var messageId = MessageWindow.RegisterWindowMessage(UniqueWindowName());
            var expectedMessage = unchecked((int)messageId);

            // The window raises MessageReceived for every dispatched message, and unrelated
            // WPF/Win32/session messages can be dispatched before the posted one. Wait for the
            // event carrying the posted identifier instead of asserting on the first event.
            var received = new TaskCompletionSource<MessageReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            await sta.InvokeAsync(() => window.MessageReceived += (_, e) =>
            {
                if (e.Message == expectedMessage)
                {
                    received.TrySetResult(e);
                }
            });

            Assert.True(MessageWindow.PostMessage(window.Handle, messageId, (nint)wParam, (nint)lParam));

            var args = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(expectedMessage, args.Message);
            Assert.Equal((nint)wParam, args.WParam);
            Assert.Equal((nint)lParam, args.LParam);
        }
        finally
        {
            await sta.InvokeAsync(window.Dispose);
        }
    }

    [Fact]
    public async Task TaskbarCreatedMessage_IsRegisteredAndObserved()
    {
        using var sta = new StaThread();
        var name = UniqueWindowName();
        var window = await sta.InvokeAsync(() => new MessageWindow(name));

        try
        {
            Assert.NotEqual(0u, window.TaskbarCreatedMessageId);
            Assert.Equal(
                MessageWindow.RegisterWindowMessage(MessageWindow.TaskbarCreatedMessageName),
                window.TaskbarCreatedMessageId);

            var raised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await sta.InvokeAsync(() => window.TaskbarCreated += (_, _) => raised.TrySetResult());

            // The shell normally broadcasts this message; posting the registered identifier
            // directly exercises the observation hook without restarting Explorer.
            Assert.True(MessageWindow.PostMessage(window.Handle, window.TaskbarCreatedMessageId));

            await raised.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await sta.InvokeAsync(window.Dispose);
        }
    }

    [Fact]
    public async Task Dispose_DestroysTheWindow()
    {
        using var sta = new StaThread();
        var name = UniqueWindowName();
        var window = await sta.InvokeAsync(() => new MessageWindow(name));

        await sta.InvokeAsync(window.Dispose);

        Assert.True(window.IsDisposed);
        Assert.Equal(nint.Zero, MessageWindow.FindMessageWindow(name));
        Assert.False(MessageWindow.PostMessage(window.Handle, MessageWindow.RegisterWindowMessage(MessageWindow.TaskbarCreatedMessageName)));
    }

    [Fact]
    public void RegisterWindowMessage_SameName_ReturnsTheSameNonZeroId()
    {
        var name = "TouchPanel.Tests.Message." + Guid.NewGuid().ToString("N");

        var first = MessageWindow.RegisterWindowMessage(name);
        var second = MessageWindow.RegisterWindowMessage(name);

        Assert.NotEqual(0u, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void PostMessage_ZeroHandle_ReturnsFalse()
    {
        Assert.False(MessageWindow.PostMessage(0, MessageWindow.RegisterWindowMessage(MessageWindow.TaskbarCreatedMessageName)));
    }

    private static string UniqueWindowName() => "TouchPanel.Tests.MessageWindow." + Guid.NewGuid().ToString("N");
}
