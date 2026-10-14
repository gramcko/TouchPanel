using TouchPanel.Actions.Handlers;
using TouchPanel.Config;

namespace TouchPanel.Tests.Actions;

/// <summary>
/// RF-048 media-key mapping: each catalog key maps to the exact Win32 virtual key, unrecognized
/// keys fail fast without synthesizing input, and a blocked injection becomes a failure result
/// (F-14).
/// </summary>
public class MediaKeyHandlerTests
{
    [Theory]
    [InlineData(MediaKeyKind.PlayPause, 0xB3)] // VK_MEDIA_PLAY_PAUSE
    [InlineData(MediaKeyKind.Next, 0xB0)] // VK_MEDIA_NEXT_TRACK
    [InlineData(MediaKeyKind.Previous, 0xB1)] // VK_MEDIA_PREV_TRACK
    [InlineData(MediaKeyKind.Stop, 0xB2)] // VK_MEDIA_STOP
    public async Task ExecuteAsync_KnownKey_SendsMappedVirtualKey(MediaKeyKind kind, int expected)
    {
        var input = new RecordingKeyInput();
        var handler = new MediaKeyHandler(input);

        var result = await handler.ExecuteAsync(new MediaKeyActionConfig { Key = kind }, default);

        Assert.True(result.Success);
        Assert.Equal((ushort)expected, Assert.Single(input.SentKeys));
    }

    [Fact]
    public async Task ExecuteAsync_UnknownKey_FailsWithoutSendingInput()
    {
        var input = new RecordingKeyInput();
        var handler = new MediaKeyHandler(input);

        var result = await handler.ExecuteAsync(new MediaKeyActionConfig { Key = MediaKeyKind.Unknown }, default);

        Assert.False(result.Success);
        Assert.Contains("Unknown", result.Message, StringComparison.Ordinal);
        Assert.Empty(input.SentKeys);
    }

    [Fact]
    public async Task ExecuteAsync_BlockedInput_ReturnsFailureWithReason()
    {
        var input = new RecordingKeyInput { Succeed = false, Error = "blocked by UIPI" };
        var handler = new MediaKeyHandler(input);

        var result = await handler.ExecuteAsync(new MediaKeyActionConfig { Key = MediaKeyKind.Next }, default);

        Assert.False(result.Success);
        Assert.Equal("blocked by UIPI", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WrongConfigType_Throws()
    {
        var handler = new MediaKeyHandler(new RecordingKeyInput());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(new MuteActionConfig(), default));
    }
}
