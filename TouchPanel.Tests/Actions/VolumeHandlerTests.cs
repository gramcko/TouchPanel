using TouchPanel.Actions.Handlers;
using TouchPanel.Config;

namespace TouchPanel.Tests.Actions;

/// <summary>
/// RF-048 volume mapping through the seams: <c>set</c> applies the absolute level via
/// <see cref="TouchPanel.Actions.IAudioController"/>, <c>step</c> sends the exact
/// <c>VK_VOLUME_UP/DOWN</c> virtual key, unrecognized modes/directions fail fast with no side
/// effects, and seam failures become failure results.
/// </summary>
public class VolumeHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_SetMode_AppliesAbsoluteLevelThroughAudioSeam()
    {
        var input = new RecordingKeyInput();
        var audio = new RecordingAudioController();
        var handler = new VolumeHandler(input, audio);

        var result = await handler.ExecuteAsync(
            new VolumeActionConfig { Mode = VolumeMode.Set, Level = 42 },
            default);

        Assert.True(result.Success);
        Assert.Equal(42, Assert.Single(audio.SetLevels));
        Assert.Empty(input.SentKeys);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-5, 0)]
    public async Task ExecuteAsync_SetMode_ClampsLevelToSaneBounds(int configured, int expected)
    {
        var audio = new RecordingAudioController();
        var handler = new VolumeHandler(new RecordingKeyInput(), audio);

        var result = await handler.ExecuteAsync(
            new VolumeActionConfig { Mode = VolumeMode.Set, Level = configured },
            default);

        Assert.True(result.Success);
        Assert.Equal(expected, Assert.Single(audio.SetLevels));
    }

    [Theory]
    [InlineData(VolumeDirection.Up, 0xAF)] // VK_VOLUME_UP
    [InlineData(VolumeDirection.Down, 0xAE)] // VK_VOLUME_DOWN
    public async Task ExecuteAsync_StepMode_SendsMappedVirtualKey(VolumeDirection direction, int expected)
    {
        var input = new RecordingKeyInput();
        var audio = new RecordingAudioController();
        var handler = new VolumeHandler(input, audio);

        var result = await handler.ExecuteAsync(
            new VolumeActionConfig { Mode = VolumeMode.Step, Direction = direction },
            default);

        Assert.True(result.Success);
        Assert.Equal((ushort)expected, Assert.Single(input.SentKeys));
        Assert.Empty(audio.SetLevels);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownMode_FailsWithoutSideEffects()
    {
        var input = new RecordingKeyInput();
        var audio = new RecordingAudioController();
        var handler = new VolumeHandler(input, audio);

        var result = await handler.ExecuteAsync(new VolumeActionConfig { Mode = VolumeMode.Unknown }, default);

        Assert.False(result.Success);
        Assert.Empty(input.SentKeys);
        Assert.Empty(audio.SetLevels);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownStepDirection_FailsWithoutSendingInput()
    {
        var input = new RecordingKeyInput();
        var handler = new VolumeHandler(input, new RecordingAudioController());

        var result = await handler.ExecuteAsync(
            new VolumeActionConfig { Mode = VolumeMode.Step, Direction = VolumeDirection.Unknown },
            default);

        Assert.False(result.Success);
        Assert.Empty(input.SentKeys);
    }

    [Fact]
    public async Task ExecuteAsync_AudioSeamFailure_ReturnsFailureWithReason()
    {
        var audio = new RecordingAudioController { Succeed = false, Error = "no output device" };
        var handler = new VolumeHandler(new RecordingKeyInput(), audio);

        var result = await handler.ExecuteAsync(
            new VolumeActionConfig { Mode = VolumeMode.Set, Level = 30 },
            default);

        Assert.False(result.Success);
        Assert.Equal("no output device", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_BlockedStepInput_ReturnsFailureWithReason()
    {
        var input = new RecordingKeyInput { Succeed = false, Error = "input blocked" };
        var handler = new VolumeHandler(input, new RecordingAudioController());

        var result = await handler.ExecuteAsync(
            new VolumeActionConfig { Mode = VolumeMode.Step, Direction = VolumeDirection.Up },
            default);

        Assert.False(result.Success);
        Assert.Equal("input blocked", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WrongConfigType_Throws()
    {
        var handler = new VolumeHandler(new RecordingKeyInput(), new RecordingAudioController());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(new MuteActionConfig(), default));
    }
}
