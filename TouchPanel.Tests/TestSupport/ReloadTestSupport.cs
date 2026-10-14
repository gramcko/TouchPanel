using TouchPanel.Services.Config;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Deterministic <see cref="IConfigReloadSource"/> double (M2-RNF-124): the test raises reload
/// requests explicitly, so the apply flow is exercised without a real <c>FileSystemWatcher</c>.
/// Tracks start/dispose so shutdown/disposal can be asserted.
/// </summary>
internal sealed class FakeConfigReloadSource : IConfigReloadSource
{
    /// <inheritdoc />
    public event EventHandler<ConfigReloadRequestedEventArgs>? ReloadRequested;

    /// <summary>Gets the number of <see cref="Start"/> calls received.</summary>
    public int StartCount { get; private set; }

    /// <summary>Gets a value indicating whether the source was disposed.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc />
    public void Start() => StartCount++;

    /// <summary>Raises a reload request on the calling thread.</summary>
    /// <param name="panelFilePath">The path reported to subscribers.</param>
    public void Raise(string panelFilePath = "panel.json") =>
        ReloadRequested?.Invoke(this, new ConfigReloadRequestedEventArgs(panelFilePath));

    /// <inheritdoc />
    public void Dispose() => Disposed = true;
}

/// <summary>
/// Deterministic candidate reader (M2-RNF-124): returns a queued/constant candidate or throws, so
/// the pipeline's validate-before-apply and failure-isolation branches are testable without I/O.
/// </summary>
internal sealed class FakePanelConfigCandidateReader : IPanelConfigCandidateReader
{
    /// <summary>Gets or sets the candidate returned by every read.</summary>
    public PanelConfigCandidate? Candidate { get; set; }

    /// <summary>Gets or sets a factory that produces a candidate per call (for advancing scenarios).</summary>
    public Func<PanelConfigCandidate>? CandidateFactory { get; set; }

    /// <summary>Gets or sets an exception thrown instead of returning a candidate.</summary>
    public Exception? ThrowOnRead { get; set; }

    /// <summary>Gets the number of read attempts.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc />
    public ValueTask<PanelConfigCandidate> ReadAsync(CancellationToken cancellationToken = default)
    {
        ReadCount++;

        if (ThrowOnRead is { } exception)
        {
            throw exception;
        }

        var candidate = CandidateFactory?.Invoke() ?? Candidate;
        if (candidate is null)
        {
            throw new InvalidOperationException("The fake reader was not configured with a candidate.");
        }

        return ValueTask.FromResult(candidate);
    }
}

/// <summary>
/// Recording <see cref="IUserNotifier"/> double: captures notifications without touching the tray
/// or any WPF type.
/// </summary>
internal sealed class RecordingUserNotifier : TouchPanel.Services.Notifications.IUserNotifier
{
    /// <summary>Gets the notifications shown, in order.</summary>
    public List<NotificationRecord> Notifications { get; } = [];

    /// <inheritdoc />
    public void Notify(string title, string message, TouchPanel.Services.Notifications.NotificationSeverity severity = TouchPanel.Services.Notifications.NotificationSeverity.Info) =>
        Notifications.Add(new NotificationRecord(title, message, severity));
}

/// <summary>One captured notification.</summary>
/// <param name="Title">The notification title.</param>
/// <param name="Message">The notification body.</param>
/// <param name="Severity">The notification severity.</param>
internal sealed record NotificationRecord(string Title, string Message, TouchPanel.Services.Notifications.NotificationSeverity Severity);
