using TouchPanel.Actions.Handlers;
using TouchPanel.Config;

namespace TouchPanel.Tests.Actions;

/// <summary>
/// RF-048 mute toggle: sends <c>VK_VOLUME_MUTE</c> through the key seam and turns a blocked
/// injection into a failure result.
/// </summary>
public class MuteHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_SendsVolumeMuteVirtualKey()
    {
        var input = new RecordingKeyInput();
        var handler = new MuteHandler(input);

        var result = await handler.ExecuteAsync(new MuteActionConfig(), default);

        Assert.True(result.Success);
        Assert.Equal((ushort)0xAD, Assert.Single(input.SentKeys)); // VK_VOLUME_MUTE
    }

    [Fact]
    public async Task ExecuteAsync_BlockedInput_ReturnsFailureWithReason()
    {
        var input = new RecordingKeyInput { Succeed = false, Error = "input blocked" };
        var handler = new MuteHandler(input);

        var result = await handler.ExecuteAsync(new MuteActionConfig(), default);

        Assert.False(result.Success);
        Assert.Equal("input blocked", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WrongConfigType_Throws()
    {
        var handler = new MuteHandler(new RecordingKeyInput());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(new MediaKeyActionConfig(), default));
    }
}
