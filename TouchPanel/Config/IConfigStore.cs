namespace TouchPanel.Config;

/// <summary>
/// Boundary for the configuration subsystem (plan §1.3, DD-9): load <c>panel.json</c> classified
/// into the four RF-075 states and save atomically with backup rotation, all through an async,
/// UI-thread-safe API (RNF-004). Configuration is read at startup and written only on defined
/// triggers; the store never polls or watches the file system (RF-080). Implementations must not
/// touch any real user profile when constructed with injected <see cref="ConfigPaths"/> (RNF-006).
/// </summary>
public interface IConfigStore
{
    /// <summary>
    /// Loads the configuration, classifying the source into exactly one of the four RF-075 states
    /// and performing the matching handling: load, migrate/validate/back up/save, or recover from
    /// the last known valid backup or generated defaults while preserving the source.
    /// </summary>
    /// <param name="cancellationToken">Cancels the file I/O; a canceled load changes nothing on disk.</param>
    /// <returns>The load result carrying the effective configuration and diagnostics.</returns>
    ValueTask<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically saves the configuration (temp file + flush + replace; RF-077). The previous
    /// successfully validated file is rotated into <c>panel.backup.json</c> before it is replaced,
    /// except when the current file is a preserved corrupt/future source (RF-078). A failure is
    /// returned, leaves the previous file intact, and is retryable on the next save.
    /// </summary>
    /// <param name="config">The configuration to persist; callers pass validated content.</param>
    /// <param name="cancellationToken">Cancels the save between the temp write and the replace.</param>
    /// <returns>The save result; check <see cref="ConfigSaveResult.Succeeded"/>.</returns>
    ValueTask<ConfigSaveResult> SaveAsync(PanelConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort emergency copy of a configuration that could not be saved during shutdown
    /// (F-08/RF-077). Writes the same JSON a save would to <c>panel.recovery.json</c> through an
    /// atomic temp-file write; <c>panel.json</c> and <c>panel.backup.json</c> are never touched.
    /// The attempt never throws and is bounded by <paramref name="cancellationToken"/>, so shutdown
    /// is never blocked indefinitely.
    /// </summary>
    /// <param name="config">The in-memory configuration whose layout must be preserved.</param>
    /// <param name="cancellationToken">Bounds the attempt against the remaining shutdown budget.</param>
    /// <returns><see langword="true"/> when the recovery copy was written; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> TryWriteRecoveryCopyAsync(PanelConfig config, CancellationToken cancellationToken = default);
}
