using TouchPanel.Actions;
using TouchPanel.Actions.Handlers;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Actions;

/// <summary>
/// RF-057/M2-RF-572/RF-576 handler behavior: modifiers are pressed before the target key and released
/// after, modifiers are always released even when the target key or a later modifier fails (no stuck
/// modifiers), and any synthesis failure becomes a failed <see cref="ActionExecutionResult"/>.
/// </summary>
public class SendKeysHandlerTests
{
    private const ushort VkControl = 0x11;
    private const ushort VkAlt = 0x12;
    private const ushort VkShift = 0x10;
    private const ushort VkA = 0x41;
    private const ushort VkS = 0x53;

    [Fact]
    public async Task ExecuteAsync_BareKey_PressesAndReleasesOnlyTheKey()
    {
        var input = new RecordingKeyInput();
        var handler = new SendKeysHandler(input);

        var result = await handler.ExecuteAsync(new SendKeysActionConfig { Key = "A" }, default);

        Assert.True(result.Success);
        Assert.Equal([VkA], input.DownKeys);
        Assert.Equal([VkA], input.UpKeys);
    }

    [Fact]
    public async Task ExecuteAsync_Modifiers_PressBeforeAndReleaseAfterInReverseOrder()
    {
        var input = new RecordingKeyInput();
        var handler = new SendKeysHandler(input);

        var result = await handler.ExecuteAsync(
            new SendKeysActionConfig { Key = "S", Modifiers = ["Shift", "Ctrl"] },
            default);

        Assert.True(result.Success);
        Assert.Equal([VkControl, VkShift, VkS], input.DownKeys);
        Assert.Equal([VkS, VkShift, VkControl], input.UpKeys);
    }

    [Fact]
    public async Task ExecuteAsync_TargetKeyDownFails_StillReleasesPressedModifiers()
    {
        var input = new RecordingKeyInput
        {
            KeyDownPredicate = virtualKey => virtualKey != VkA,
        };
        var handler = new SendKeysHandler(input);

        var result = await handler.ExecuteAsync(
            new SendKeysActionConfig { Key = "A", Modifiers = ["Alt"] },
            default);

        Assert.False(result.Success);
        Assert.False(result.Canceled);

        // Alt was pressed, the target key down was attempted, and Alt was released (no stuck modifier).
        Assert.Equal([VkAlt, VkA], input.DownKeys);
        Assert.Equal([VkAlt], input.UpKeys);
    }

    [Fact]
    public async Task ExecuteAsync_SecondModifierFails_ReleasesAlreadyPressedModifiers()
    {
        var input = new RecordingKeyInput
        {
            KeyDownPredicate = virtualKey => virtualKey != VkAlt,
        };
        var handler = new SendKeysHandler(input);

        var result = await handler.ExecuteAsync(
            new SendKeysActionConfig { Key = "A", Modifiers = ["Alt", "Ctrl"] },
            default);

        Assert.False(result.Success);
        Assert.Equal([VkControl, VkAlt], input.DownKeys);
        Assert.Equal([VkControl], input.UpKeys);
    }

    [Fact]
    public async Task ExecuteAsync_BlockedInput_ReturnsFailureWithReason()
    {
        var input = new RecordingKeyInput { Succeed = false, Error = "blocked by UIPI" };
        var handler = new SendKeysHandler(input);

        var result = await handler.ExecuteAsync(new SendKeysActionConfig { Key = "A" }, default);

        Assert.False(result.Success);
        Assert.Equal("blocked by UIPI", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownKey_FailsWithoutSynthesizingInput()
    {
        var input = new RecordingKeyInput();
        var handler = new SendKeysHandler(input);

        var result = await handler.ExecuteAsync(new SendKeysActionConfig { Key = "Foo" }, default);

        Assert.False(result.Success);
        Assert.Empty(input.DownKeys);
        Assert.Empty(input.UpKeys);
    }

    [Fact]
    public async Task ExecuteAsync_PreCanceledToken_ThrowsOperationCanceled()
    {
        var handler = new SendKeysHandler(new RecordingKeyInput());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.ExecuteAsync(new SendKeysActionConfig { Key = "A" }, cancellation.Token));
    }

    [Fact]
    public async Task ExecuteAsync_WrongConfigType_Throws()
    {
        var handler = new SendKeysHandler(new RecordingKeyInput());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(new MuteActionConfig(), default));
    }

    [Fact]
    public async Task ExecuteAsync_InputSynthesis_RunsOffTheUiDispatcherThread()
    {
        // Regression: a tap used to synthesize keys synchronously on the WPF dispatcher thread, so a
        // slow/blocked SendInput froze the UI while the modifiers were already down. The synthesis
        // must run on the thread pool, and the combination must stay fully balanced and ordered.
        using var sta = new StaThread();
        var dispatcher = await sta.InvokeAsync(() => System.Windows.Threading.Dispatcher.CurrentDispatcher);

        var onUiThread = new List<bool>();
        var input = new RecordingKeyInput { OnKey = () => onUiThread.Add(dispatcher.CheckAccess()) };
        var handler = new SendKeysHandler(input);

        Task<ActionExecutionResult> execution = null!;
        await sta.InvokeAsync(() =>
        {
            execution = handler.ExecuteAsync(new SendKeysActionConfig { Key = "S", Modifiers = ["Ctrl", "Shift"] }, default);
        });
        var result = await execution;

        Assert.True(result.Success);
        Assert.NotEmpty(onUiThread);
        Assert.All(onUiThread, value => Assert.False(value));

        Assert.Equal([VkControl, VkShift, VkS], input.DownKeys);
        Assert.Equal([VkS, VkShift, VkControl], input.UpKeys);
    }
}
