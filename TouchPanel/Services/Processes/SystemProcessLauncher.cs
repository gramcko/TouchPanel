using System.Diagnostics;

namespace TouchPanel.Services.Processes;

/// <summary>
/// Production <see cref="IProcessLauncher"/> (DD-8): starts an explicit executable with discrete
/// arguments through <c>ProcessStartInfo</c>, always with <c>UseShellExecute = false</c> (RF-049,
/// D-7, RNF-014). No shell is consulted, so hostile argument strings stay literal data and no
/// <c>cmd.exe</c>/PowerShell wrapper is ever introduced.
/// </summary>
/// <remarks>
/// <para><b>No waiting (D-20/RF-052).</b> The started <see cref="Process"/> is disposed immediately
/// after launch; disposing releases only our handle and never affects the external process, which
/// keeps running untracked. No exit code is observed and nothing is killed on shutdown.</para>
/// <para><b>No UI-thread blocking (RF-050/RNF-004).</b> <c>Process.Start</c> has no asynchronous
/// form, so it runs on the thread pool; callers always await a task.</para>
/// </remarks>
public sealed class SystemProcessLauncher : IProcessLauncher
{
    /// <inheritdoc />
    public Task<ProcessLaunchResult> LaunchAsync(
        ProcessLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);

        return Task.Run(() => Launch(request), cancellationToken);
    }

    /// <summary>
    /// Builds the exact <see cref="ProcessStartInfo"/> used for a launch. Exposed to tests so the
    /// <c>UseShellExecute = false</c> and discrete-argument invariants are verifiable without
    /// starting a process.
    /// </summary>
    /// <param name="request">The validated launch request.</param>
    /// <returns>The start info with the shell disabled and each argument added discretely.</returns>
    internal static ProcessStartInfo CreateStartInfo(ProcessLaunchRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,

            // RF-049/D-7: never route through the shell (no cmd.exe, no document/URL launching).
            UseShellExecute = false,
        };

        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.ArgumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static ProcessLaunchResult Launch(ProcessLaunchRequest request)
    {
        try
        {
            using var process = Process.Start(CreateStartInfo(request));
            return process is null
                ? ProcessLaunchResult.Failed("The process did not start.")
                : ProcessLaunchResult.Ok();
        }
        catch (Exception exception)
        {
            // Missing executable, access denied, invalid working directory, ... (RF-050/F-13).
            return ProcessLaunchResult.Failed($"{exception.GetType().Name}: {exception.Message}");
        }
    }
}
