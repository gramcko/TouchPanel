namespace TouchPanel.Config;

/// <summary>
/// Seam that lets every application-originated configuration/runtime write open a self-write
/// window (M2-RF-823): the config store (<see cref="JsonConfigStore.SaveAsync"/>) and the runtime
/// state store (<see cref="StateStore.SaveAsync"/>) mark the window around their atomic write so
/// the hot-reload decision component ignores the resulting file-system events instead of
/// self-triggering a reload. The contract is deliberately minimal and file-system agnostic so the
/// stores stay unit-testable with a fake (RNF-006/M2-RNF-124); the composition root supplies the
/// real implementation (the reload decision engine).
/// </summary>
/// <remarks>
/// The stores accept this optionally. When it is <see langword="null"/> the store behaves exactly
/// as before (no suppression), which keeps every existing call site and test unaffected.
/// </remarks>
public interface IAppWriteSuppression
{
    /// <summary>
    /// Opens a self-write scope around one application write. The scope must be disposed when the
    /// write finishes, on both the success and failure paths, so the suppression window is closed
    /// deterministically. Nested/overlapping writes are supported.
    /// </summary>
    /// <returns>A scope that owns the write window; never <see langword="null"/>.</returns>
    IAppWriteScope BeginWrite();

    /// <summary>
    /// Seeds the last-applied content baseline without opening a self-write window, so a
    /// file-system event carrying byte-identical content is a no-op from the first event even on a
    /// cold start where the application has not yet saved <c>panel.json</c> (M2-RF-823). The
    /// configuration store calls this after loading an already-current <c>panel.json</c>.
    /// </summary>
    /// <param name="content">The exact UTF-8 text of the configuration that is now running.</param>
    void RecordAppliedContent(string content);
}

/// <summary>
/// One application-write suppression window (M2-RF-823). Disposing the scope ends the write and
/// keeps a short post-write window open so file-system events raised slightly after the atomic
/// replace are still suppressed. The optional <see cref="RecordContent"/> lets the configuration
/// store contribute the exact bytes it wrote so a later event with byte-identical content is a
/// no-op by hash comparison.
/// </summary>
public interface IAppWriteScope : IDisposable
{
    /// <summary>
    /// Records the exact configuration content written, enabling the content-unchanged no-op
    /// (M2-RF-823). Must be called before the scope is disposed. State-store writes need not call
    /// it because <c>state.json</c> is never watched for reloads (M2-RF-821).
    /// </summary>
    /// <param name="content">The exact UTF-8 text persisted to <c>panel.json</c>.</param>
    void RecordContent(string content);
}
