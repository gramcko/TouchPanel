using TouchPanel.Config;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IConfigStore"/> double for edit-mode tests (T-12): records every save
/// attempt and the exact configuration instance handed to the store, and can simulate success,
/// store failures, and thrown exceptions without touching the file system. Loading is not part of
/// these tests and deliberately fails loudly.
/// </summary>
internal sealed class RecordingConfigStore : IConfigStore
{
    /// <summary>Gets the number of <see cref="SaveAsync"/> calls that reached the store.</summary>
    public int SaveCallCount { get; private set; }

    /// <summary>Gets the number of <see cref="TryWriteRecoveryCopyAsync"/> calls that reached the store.</summary>
    public int RecoveryCopyCallCount { get; private set; }

    /// <summary>Gets the last configuration instance passed to <see cref="TryWriteRecoveryCopyAsync"/>.</summary>
    public PanelConfig? LastRecoveryCopyConfig { get; private set; }

    /// <summary>Gets or sets the result returned by <see cref="TryWriteRecoveryCopyAsync"/>.</summary>
    public bool RecoveryCopySucceeds { get; set; } = true;

    /// <summary>Gets the last configuration instance passed to <see cref="SaveAsync"/>.</summary>
    public PanelConfig? LastSavedConfig { get; private set; }

    /// <summary>Gets or sets the result returned by <see cref="SaveAsync"/> when no handler is set.</summary>
    public bool SaveSucceeds { get; set; } = true;

    /// <summary>Gets or sets the failure message used when <see cref="SaveSucceeds"/> is <see langword="false"/>.</summary>
    public string FailureMessage { get; set; } = "The configuration could not be saved (simulated).";

    /// <summary>Gets or sets an override that produces the save result (or throws) for a call.</summary>
    public Func<PanelConfig, ConfigSaveResult>? SaveHandler { get; set; }

    /// <summary>
    /// Gets or sets an asynchronous override used to observe in-flight saves (for example a gate
    /// that keeps a save pending while the edit-mode flag is asserted).
    /// </summary>
    public Func<PanelConfig, ValueTask<ConfigSaveResult>>? SaveAsyncHandler { get; set; }

    /// <inheritdoc />
    public ValueTask<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Edit-mode tests do not use the load path.");

    /// <inheritdoc />
    public ValueTask<ConfigSaveResult> SaveAsync(PanelConfig config, CancellationToken cancellationToken = default)
    {
        SaveCallCount++;
        LastSavedConfig = config;

        if (SaveAsyncHandler is { } asyncHandler)
        {
            return asyncHandler(config);
        }

        var result = SaveHandler is { } handler
            ? handler(config)
            : SaveSucceeds
                ? ConfigSaveResult.Success()
                : ConfigSaveResult.Failure("test.save.failed", FailureMessage);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public ValueTask<bool> TryWriteRecoveryCopyAsync(
        PanelConfig config,
        CancellationToken cancellationToken = default)
    {
        RecoveryCopyCallCount++;
        LastRecoveryCopyConfig = config;
        return ValueTask.FromResult(RecoveryCopySucceeds);
    }
}
