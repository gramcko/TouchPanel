namespace TouchPanel.Services.Updates;

/// <summary>The classified outcome of one manual update check.</summary>
public enum UpdateCheckStatus
{
    /// <summary>The latest published release is the running version or older.</summary>
    UpToDate,

    /// <summary>A newer release exists.</summary>
    UpdateAvailable,

    /// <summary>The repository has no published releases (GitHub answered 404).</summary>
    NoReleases,

    /// <summary>The check could not complete: offline, timeout, HTTP error, or malformed payload.</summary>
    Failed,
}

/// <summary>
/// Immutable result of a manual update check. It carries only data; presentation belongs to
/// <see cref="ViewModels.About.AboutViewModel"/>.
/// </summary>
/// <param name="Status">The classified outcome.</param>
/// <param name="LatestVersion">The parsed latest release version when one was found.</param>
/// <param name="ReleaseUrl">The HTTPS release page when the payload supplied one.</param>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    VersionTag? LatestVersion = null,
    string? ReleaseUrl = null)
{
    /// <summary>Creates an up-to-date result.</summary>
    /// <param name="latestVersion">The latest released version, when known.</param>
    /// <returns>The result.</returns>
    public static UpdateCheckResult UpToDate(VersionTag? latestVersion = null) =>
        new(UpdateCheckStatus.UpToDate, latestVersion);

    /// <summary>Creates an update-available result.</summary>
    /// <param name="latestVersion">The newer released version.</param>
    /// <param name="releaseUrl">The HTTPS release page, when supplied.</param>
    /// <returns>The result.</returns>
    public static UpdateCheckResult UpdateAvailable(VersionTag latestVersion, string? releaseUrl) =>
        new(UpdateCheckStatus.UpdateAvailable, latestVersion, releaseUrl);

    /// <summary>Creates a no-releases result.</summary>
    /// <returns>The result.</returns>
    public static UpdateCheckResult NoReleases() => new(UpdateCheckStatus.NoReleases);

    /// <summary>Creates a failed result.</summary>
    /// <returns>The result.</returns>
    public static UpdateCheckResult Failed() => new(UpdateCheckStatus.Failed);
}
