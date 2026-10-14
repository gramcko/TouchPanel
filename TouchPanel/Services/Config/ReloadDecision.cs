using System.IO;
using System.Security.Cryptography;
using System.Text;
using TouchPanel.Config;

namespace TouchPanel.Services.Config;

/// <summary>
/// Outcome of the pure reload decision (M2-RF-830): ignore the signal, keep debouncing/coalescing,
/// or reload from the watched file.
/// </summary>
public enum ReloadDecision
{
    /// <summary>The signal must not lead to a reload (not the effective file, self-write, unchanged, or still coalescing).</summary>
    Ignore,

    /// <summary>A reload is pending but the debounce window has not elapsed yet; wait/reschedule.</summary>
    Debounce,

    /// <summary>The debounce elapsed and the change must be read and applied (after whole-file validation).</summary>
    Reload,
}

/// <summary>Normalized kind of a watched-file change (M2-RF-830): the OS watcher maps its raw events onto this.</summary>
public enum ConfigFileChangeKind
{
    /// <summary>A new file appeared.</summary>
    Created,

    /// <summary>An existing file was written/changed.</summary>
    Changed,

    /// <summary>A file was renamed into place.</summary>
    Renamed,

    /// <summary>A file was deleted.</summary>
    Deleted,

    /// <summary>The watcher itself reported an error (for example a buffer overflow).</summary>
    Error,
}

/// <summary>
/// A normalized file-system change fed to <see cref="ReloadDecisionEngine"/> (M2-RF-830). Carries
/// only data; the engine never performs I/O.
/// </summary>
/// <param name="Kind">The kind of change.</param>
/// <param name="Path">The affected path, or <see langword="null"/> for watcher errors.</param>
/// <param name="Timestamp">The moment the event was observed (for logging/telemetry).</param>
public readonly record struct ConfigFileChange(
    ConfigFileChangeKind Kind,
    string? Path,
    DateTimeOffset Timestamp);

/// <summary>
/// Pure, injectable reload-decision component (M2-RF-830, MD-9). Given a normalized file-system
/// event plus the application's last-written content hash, an app-originated self-write window,
/// and the in-flight-write state, it decides <see cref="ReloadDecision.Ignore"/>,
/// <see cref="ReloadDecision.Debounce"/>, or <see cref="ReloadDecision.Reload"/>. It performs no
/// I/O, holds no OS handles, and reads the clock only through the injected
/// <see cref="TimeProvider"/>, so all hot-reload semantics — self-write suppression for the config
/// <em>and</em> state stores, coalescing, and the content-unchanged no-op — are deterministic in
/// unit tests (RNF-006/M2-RNF-124).
/// </summary>
/// <remarks>
/// The OS <see cref="System.IO.FileSystemWatcher"/> wrapper (<c>ConfigReloadController</c>) and the
/// stores are thin adapters that call into this engine. After a debounced
/// <see cref="ReloadDecision.Reload"/>, the caller reads the candidate file and passes its content
/// to <see cref="EvaluateCandidate"/>; a byte-identical candidate returns
/// <see cref="ReloadDecision.Ignore"/> so no apply work is done (M2-RF-823).
/// </remarks>
public sealed class ReloadDecisionEngine : IAppWriteSuppression, IDisposable
{
    /// <summary>Default coalescing window (M2-RF-821): repeated edits collapse into one reload attempt.</summary>
    public static readonly TimeSpan DefaultDebounceWindow = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Default self-write window (M2-RF-823): events arriving within this period after an
    /// application write are ignored even if the watcher raises them slightly after the atomic
    /// replace completes.
    /// </summary>
    public static readonly TimeSpan DefaultSelfWriteWindow = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly string _effectiveFileName;

    private string? _baselineContentHash;
    private int _inFlightWrites;
    private DateTimeOffset _suppressionUntil = DateTimeOffset.MinValue;
    private bool _reloadPending;
    private DateTimeOffset _debounceDue;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReloadDecisionEngine"/> class.
    /// </summary>
    /// <param name="timeProvider">Clock used for the debounce/self-write windows; defaults to the system clock.</param>
    /// <param name="debounceWindow">Coalescing window; defaults to <see cref="DefaultDebounceWindow"/>.</param>
    /// <param name="selfWriteWindow">Post-write suppression window; defaults to <see cref="DefaultSelfWriteWindow"/>.</param>
    /// <param name="effectiveFileName">Watched file name; defaults to <c>panel.json</c> (M2-RF-821).</param>
    public ReloadDecisionEngine(
        TimeProvider? timeProvider = null,
        TimeSpan? debounceWindow = null,
        TimeSpan? selfWriteWindow = null,
        string? effectiveFileName = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        DebounceWindow = debounceWindow ?? DefaultDebounceWindow;
        SelfWriteWindow = selfWriteWindow ?? DefaultSelfWriteWindow;
        _effectiveFileName = string.IsNullOrWhiteSpace(effectiveFileName)
            ? ConfigPaths.PanelFileName
            : effectiveFileName;
    }

    /// <summary>Gets the configured coalescing window.</summary>
    public TimeSpan DebounceWindow { get; }

    /// <summary>Gets the configured post-write suppression window.</summary>
    public TimeSpan SelfWriteWindow { get; }

    /// <summary>Gets a value indicating whether a reload is pending (a debounce is armed) and the engine is still live.</summary>
    public bool HasPendingReload
    {
        get
        {
            lock (_gate)
            {
                return _reloadPending && !_disposed;
            }
        }
    }

    /// <inheritdoc />
    public IAppWriteScope BeginWrite()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return NoOpAppWriteScope.Instance;
            }

            _inFlightWrites++;

            // Open the window at the start as well, so an event raised mid-write is ignored even
            // before the scope is disposed.
            var now = _timeProvider.GetUtcNow();
            var until = now + SelfWriteWindow;
            if (until > _suppressionUntil)
            {
                _suppressionUntil = until;
            }

            return new WriteScope(this);
        }
    }

    /// <summary>
    /// Records the content of a configuration that was successfully applied from an external file,
    /// so a later event carrying the same bytes is a no-op (M2-RF-823). Called by the apply
    /// pipeline (part 2) after a successful reload/import.
    /// </summary>
    /// <param name="content">The applied configuration text.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is <see langword="null"/>.</exception>
    public void RecordAppliedContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        lock (_gate)
        {
            _baselineContentHash = ComputeContentHash(content);
        }
    }

    /// <summary>
    /// Feeds one normalized file-system event and returns the immediate decision. A signal for any
    /// other file, an event during an application write, or an event inside the self-write window
    /// is <see cref="ReloadDecision.Ignore"/>; otherwise the engine arms/resets the debounce and
    /// returns <see cref="ReloadDecision.Debounce"/> (M2-RF-821/M2-RF-823).
    /// </summary>
    /// <param name="change">The normalized change; the engine never touches the file system.</param>
    /// <returns>The immediate decision for the event.</returns>
    public ReloadDecision NotifyChange(in ConfigFileChange change)
    {
        lock (_gate)
        {
            if (_disposed || !IsEffectiveFile(change.Path))
            {
                return ReloadDecision.Ignore;
            }

            if (_inFlightWrites > 0)
            {
                return ReloadDecision.Ignore;
            }

            var now = _timeProvider.GetUtcNow();
            if (now < _suppressionUntil)
            {
                return ReloadDecision.Ignore;
            }

            // Coalesce: every fresh event pushes the due time out, so rapid edits collapse into a
            // single reload attempt once editing stops (M2-RF-821).
            _reloadPending = true;
            _debounceDue = now + DebounceWindow;
            return ReloadDecision.Debounce;
        }
    }

    /// <summary>
    /// Called when the debounce timer fires. Returns <see cref="ReloadDecision.Reload"/> only when a
    /// reload is still pending, the debounce window has fully elapsed, and no application write is
    /// in flight or inside the self-write window; otherwise it clears/settles the pending state and
    /// returns <see cref="ReloadDecision.Ignore"/> (or <see cref="ReloadDecision.Debounce"/> when the
    /// timer fired early and must be rescheduled).
    /// </summary>
    /// <returns>The decision after the debounce.</returns>
    public ReloadDecision NotifyDebounceElapsed()
    {
        lock (_gate)
        {
            if (_disposed || !_reloadPending)
            {
                return ReloadDecision.Ignore;
            }

            var now = _timeProvider.GetUtcNow();
            if (now < _debounceDue)
            {
                return ReloadDecision.Debounce;
            }

            _reloadPending = false;

            if (_inFlightWrites > 0 || now < _suppressionUntil)
            {
                return ReloadDecision.Ignore;
            }

            return ReloadDecision.Reload;
        }
    }

    /// <summary>
    /// Evaluates a freshly read candidate against the last content the application wrote/applied.
    /// Byte-identical content is a no-op (<see cref="ReloadDecision.Ignore"/>); any other content is
    /// a <see cref="ReloadDecision.Reload"/> candidate that the apply pipeline must still validate as
    /// a whole (M2-RF-822/M2-RF-823).
    /// </summary>
    /// <param name="content">The candidate file text; must not be <see langword="null"/>.</param>
    /// <returns>The decision for the candidate content.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is <see langword="null"/>.</exception>
    public ReloadDecision EvaluateCandidate(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        lock (_gate)
        {
            if (_disposed)
            {
                return ReloadDecision.Ignore;
            }

            var hash = ComputeContentHash(content);
            return string.Equals(hash, _baselineContentHash, StringComparison.Ordinal)
                ? ReloadDecision.Ignore
                : ReloadDecision.Reload;
        }
    }

    private static string ComputeContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private bool IsEffectiveFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, _effectiveFileName, StringComparison.OrdinalIgnoreCase);
    }

    private void EndWrite()
    {
        lock (_gate)
        {
            if (_inFlightWrites > 0)
            {
                _inFlightWrites--;
            }

            var until = _timeProvider.GetUtcNow() + SelfWriteWindow;
            if (until > _suppressionUntil)
            {
                _suppressionUntil = until;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _reloadPending = false;
        }
    }

    private sealed class WriteScope : IAppWriteScope
    {
        private readonly ReloadDecisionEngine _owner;
        private bool _completed;

        public WriteScope(ReloadDecisionEngine owner) => _owner = owner;

        public void RecordContent(string content)
        {
            ArgumentNullException.ThrowIfNull(content);

            if (_completed)
            {
                return;
            }

            lock (_owner._gate)
            {
                _owner._baselineContentHash = ComputeContentHash(content);
            }
        }

        public void Dispose()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _owner.EndWrite();
        }
    }

    private sealed class NoOpAppWriteScope : IAppWriteScope
    {
        public static readonly NoOpAppWriteScope Instance = new();

        public void RecordContent(string content)
        {
        }

        public void Dispose()
        {
        }
    }
}
