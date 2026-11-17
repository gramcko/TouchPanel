namespace TouchPanel.Services.Updates;

/// <summary>
/// The injectable seam for a manual update check. Implementations never throw for ordinary network
/// or payload failures; they return <see cref="UpdateCheckStatus.Failed"/> so the About window can
/// stay open and show a neutral message. The production implementation is
/// <see cref="GitHubUpdateCheckService"/>; tests substitute a fake and never touch the network.
/// </summary>
public interface IUpdateCheckService
{
    /// <summary>Checks the latest published release against the running version.</summary>
    /// <param name="cancellationToken">Cancels the check (for example when the window closes).</param>
    /// <returns>The classified result; never <see langword="null"/>.</returns>
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default);
}
