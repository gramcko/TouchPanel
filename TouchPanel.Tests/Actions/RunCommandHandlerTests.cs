using TouchPanel.Actions.Handlers;
using TouchPanel.Config;
using TouchPanel.Services.Processes;

namespace TouchPanel.Tests.Actions;

/// <summary>
/// RF-048/RF-049 run-command handler behavior with a fake launcher: the validated request reaches
/// the launcher with the executable, discrete arguments, and working directory intact; launcher
/// failures become failure results; an invalid path fails fast without launching.
/// </summary>
public class RunCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_ValidConfig_LaunchesDiscreteRequest()
    {
        var launcher = new RecordingProcessLauncher();
        var handler = new RunCommandHandler(launcher);
        var action = new RunCommandActionConfig
        {
            Path = @"C:\Program Files\My App\app.exe",
            Arguments = ["--token=supersecret", "a b"],
            WorkingDirectory = @"C:\work dir",
        };

        var result = await handler.ExecuteAsync(action, default);

        Assert.True(result.Success);
        var request = Assert.Single(launcher.Requests);
        Assert.Equal(@"C:\Program Files\My App\app.exe", request.FileName);
        Assert.Equal(["--token=supersecret", "a b"], request.ArgumentList);
        Assert.Equal(@"C:\work dir", request.WorkingDirectory);
    }

    [Fact]
    public async Task ExecuteAsync_LauncherFailure_ReturnsFailureWithReason()
    {
        var launcher = new RecordingProcessLauncher
        {
            Result = ProcessLaunchResult.Failed("Win32Exception: cannot find the file"),
        };
        var handler = new RunCommandHandler(launcher);

        var result = await handler.ExecuteAsync(new RunCommandActionConfig { Path = "missing.exe" }, default);

        Assert.False(result.Success);
        Assert.Equal("Win32Exception: cannot find the file", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyPath_FailsFastWithoutLaunching()
    {
        var launcher = new RecordingProcessLauncher();
        var handler = new RunCommandHandler(launcher);

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.ExecuteAsync(new RunCommandActionConfig { Path = "   " }, default));

        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_WrongConfigType_Throws()
    {
        var handler = new RunCommandHandler(new RecordingProcessLauncher());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(new MuteActionConfig(), default));
    }
}
