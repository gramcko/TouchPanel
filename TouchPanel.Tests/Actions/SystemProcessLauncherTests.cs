using System.IO;
using TouchPanel.Services.Processes;

namespace TouchPanel.Tests.Actions;

/// <summary>
/// RF-049 process-launch invariants at the <see cref="IProcessLauncher"/> boundary:
/// <c>UseShellExecute = false</c>, arguments passed discretely through <c>ArgumentList</c> (no
/// command-line string), an explicit system executable starts without a shell, and launch failures
/// are results instead of exceptions (RF-050).
/// </summary>
public class SystemProcessLauncherTests
{
    [Fact]
    public void CreateStartInfo_DisablesShellAndKeepsArgumentsDiscrete()
    {
        var request = new ProcessLaunchRequest(
            @"C:\Program Files\My App\app.exe",
            ["a b", "\"quoted\"", "--flag=1"],
            @"C:\work dir");

        var startInfo = SystemProcessLauncher.CreateStartInfo(request);

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(@"C:\Program Files\My App\app.exe", startInfo.FileName);
        Assert.Equal(@"C:\work dir", startInfo.WorkingDirectory);
        Assert.Equal(["a b", "\"quoted\"", "--flag=1"], startInfo.ArgumentList);
        Assert.True(string.IsNullOrEmpty(startInfo.Arguments), "ArgumentList mode must not build a command-line string.");
    }

    [Fact]
    public void CreateStartInfo_NoWorkingDirectory_LeavesItUnset()
    {
        var startInfo = SystemProcessLauncher.CreateStartInfo(new ProcessLaunchRequest("tool.exe", []));

        Assert.Equal(string.Empty, startInfo.WorkingDirectory);
    }

    [Fact]
    public async Task LaunchAsync_ExplicitSystemExecutable_StartsWithoutWaiting()
    {
        var where = Path.Combine(Environment.SystemDirectory, "where.exe");
        Assert.True(File.Exists(where), $"Expected the test environment to provide '{where}'.");

        var launcher = new SystemProcessLauncher();

        var result = await launcher.LaunchAsync(new ProcessLaunchRequest(where, ["where.exe"]));

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task LaunchAsync_MissingExecutable_ReturnsFailureWithoutThrowing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"touchpanel-missing-{Guid.NewGuid():N}.exe");
        var launcher = new SystemProcessLauncher();

        var result = await launcher.LaunchAsync(new ProcessLaunchRequest(missing, []));

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task LaunchAsync_EmptyFileName_Throws()
    {
        var launcher = new SystemProcessLauncher();

        await Assert.ThrowsAsync<ArgumentException>(() => launcher.LaunchAsync(new ProcessLaunchRequest("   ", [])));
    }
}
