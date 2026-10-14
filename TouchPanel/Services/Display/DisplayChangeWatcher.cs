using Serilog;

namespace TouchPanel.Services.Display;

/// <summary>
/// Reacts to OS display-configuration changes (RF-022) and raises one coalesced
/// <see cref="DisplayChanged"/> event per burst on the thread selected by the injected marshaller.
/// It subscribes to an <see cref="IDisplayChangeSource"/> (production:
/// <see cref="SystemEventsDisplayChangeSource"/>) and performs no polling and no scheduling of its
/// own (RNF-008); callers re-evaluate monitor availability on demand when the event arrives.
/// </summary>
/// <remarks>
/// <para><b>Threading contract.</b> <see cref="IDisplayChangeSource.Changed"/> may be raised on any
/// thread (the SystemEvents notification thread in production). The watcher never runs subscriber
/// code on that thread: it posts the raise through the constructor's <c>post</c> delegate. The
/// composition root passes the WPF dispatcher (<c>action =&gt; Dispatcher.BeginInvoke(action)</c>),
/// so <see cref="DisplayChanged"/> subscribers may touch UI objects; when no delegate is supplied
/// (tests, headless use) the event is raised synchronously on the source's thread.</para>
/// <para><b>Bursts.</b> While a raise is already scheduled, additional source notifications are
/// dropped: several OS messages for one user-visible change produce a single event, and every
/// subscriber failure is isolated and logged so future changes keep flowing (RF-012/RNF-013).</para>
/// </remarks>
public sealed class DisplayChangeWatcher : IDisposable
{
    private readonly IDisplayChangeSource _source;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;
    private int _pending;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisplayChangeWatcher"/> class and starts
    /// observing the source.
    /// </summary>
    /// <param name="source">The display-change source the watcher assumes ownership of and disposes.</param>
    /// <param name="logger">Logger for isolated failures; defaults to the global Serilog logger.</param>
    /// <param name="post">
    /// Marshals a callback to the thread that owns <see cref="DisplayChanged"/> subscribers; when
    /// <see langword="null"/>, callbacks run synchronously on the source's thread.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    public DisplayChangeWatcher(IDisplayChangeSource source, ILogger? logger = null, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _post = post ?? (static action => action());
        _logger = (logger ?? Serilog.Log.Logger).ForContext<DisplayChangeWatcher>();
        _source.Changed += OnSourceChanged;
    }

    /// <summary>
    /// Raised (at most once per burst) when the OS reported a display-configuration change. The
    /// event is marshalled through the constructor's <c>post</c> delegate; subscriber exceptions
    /// are caught and logged.
    /// </summary>
    public event EventHandler? DisplayChanged;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _source.Changed -= OnSourceChanged;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Display change: unsubscribing from the display-change source failed");
        }

        try
        {
            _source.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Display change: disposing the display-change source failed");
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _pending, 1) != 0)
        {
            // A raise is already queued for this burst; one event is enough because handling is
            // idempotent and re-reads the current monitor state.
            return;
        }

        try
        {
            _post(RaiseDisplayChanged);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _pending, 0);
            _logger.Warning(ex, "Display change: marshalling the display-change notification failed; the event was dropped");
        }
    }

    private void RaiseDisplayChanged()
    {
        Interlocked.Exchange(ref _pending, 0);

        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _logger.Information("Display change: the OS reported a display settings change");

        try
        {
            DisplayChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // A subscriber failure (for example panel re-placement) must never reach the
            // SystemEvents notification thread or stop later display-change handling (RNF-013).
            _logger.Error(ex, "Display change: a subscriber failed while handling the display-change event");
        }
    }
}
