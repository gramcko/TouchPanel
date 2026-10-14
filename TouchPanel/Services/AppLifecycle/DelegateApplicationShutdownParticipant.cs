namespace TouchPanel.Services.AppLifecycle;

/// <summary>
/// Adapts a delegate to <see cref="IApplicationShutdownParticipant"/> so the composition root can
/// register a disposable subsystem (for example the single-instance service) without a dedicated
/// participant class.
/// </summary>
public sealed class DelegateApplicationShutdownParticipant : IApplicationShutdownParticipant
{
    private readonly Func<CancellationToken, ValueTask> _shutdown;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateApplicationShutdownParticipant"/> class.
    /// </summary>
    /// <param name="name">The stable participant name used in shutdown logs.</param>
    /// <param name="shutdown">The cleanup delegate; must complete or honor the cancellation token.</param>
    public DelegateApplicationShutdownParticipant(string name, Func<CancellationToken, ValueTask> shutdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shutdown);

        Name = name;
        _shutdown = shutdown;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public ValueTask ShutdownAsync(CancellationToken cancellationToken) => _shutdown(cancellationToken);
}
