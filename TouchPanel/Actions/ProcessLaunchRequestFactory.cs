using TouchPanel.Config;
using TouchPanel.Services.Processes;

namespace TouchPanel.Actions;

/// <summary>
/// Pure builder for <see cref="ProcessLaunchRequest"/> (DD-8). This is the security-critical part of
/// process launching (RF-049/RNF-014): the configured executable becomes the request file name and
/// every configured argument is copied as a discrete value. The factory never concatenates a
/// command string, never parses or re-quotes arguments, and never introduces <c>cmd.exe</c> or
/// PowerShell; the shell is only ever bypassed further down by
/// <c>ProcessStartInfo.UseShellExecute = false</c> (D-7).
/// </summary>
/// <remarks>
/// The factory treats configuration as untrusted data (RF-081). Hostile argument strings such as
/// <c>&amp;&amp; calc</c>, <c>$(whoami)</c> or embedded quotes are passed through literally; they
/// cannot become commands because nothing ever interprets them as one.
/// </remarks>
public static class ProcessLaunchRequestFactory
{
    /// <summary>
    /// Builds a validated launch request from a run-command action config.
    /// </summary>
    /// <param name="config">The run-command configuration; its executable path must be non-empty.</param>
    /// <returns>The launch request with the executable, the copied discrete arguments, and the optional working directory.</returns>
    /// <exception cref="ArgumentException"><paramref name="config"/> has an empty executable path (fail fast; T-04 validation should have disabled such a widget).</exception>
    public static ProcessLaunchRequest Create(RunCommandActionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.Path))
        {
            throw new ArgumentException(
                "The run-command action requires a non-empty executable path.",
                nameof(config));
        }

        return new ProcessLaunchRequest(
            FileName: config.Path,
            ArgumentList: config.Arguments.ToArray(),
            WorkingDirectory: string.IsNullOrWhiteSpace(config.WorkingDirectory) ? null : config.WorkingDirectory);
    }
}
