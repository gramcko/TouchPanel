using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace TouchPanel.Config;

/// <summary>
/// Persists the most-recently-used colour list in the existing <c>state.json</c> runtime store
/// (user-requested colour picker). It reuses the same debounced, failure-tolerant write pattern as
/// <see cref="ToggleStatePersistence"/>: at most one atomic save per interval while colours keep
/// being picked, one final flush on shutdown, and a failed write is logged and retried later.
/// </summary>
/// <remarks>
/// The class is file-system independent (it is constructed with the loaded
/// <see cref="RuntimeState"/> and a save delegate), so the MRU/debounce semantics stay unit-testable
/// with a fake clock and a counting save. The MRU rules themselves live in
/// <see cref="RecentColorPalette"/>; this adapter only copies the resulting list into the runtime
/// state and schedules the write.
/// </remarks>
public sealed class RecentColorPersistence : IDisposable, IRecentColorPalette
{
    /// <summary>Debounce window: one write per this interval while selections keep arriving.</summary>
    public static readonly TimeSpan DefaultWriteInterval = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly RuntimeState _state;
    private readonly RecentColorPalette _palette;
    private readonly Func<RuntimeState, CancellationToken, ValueTask<bool>> _save;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _writeInterval;
    private readonly ILogger _logger;
    private ITimer? _timer;
    private bool _dirty;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RecentColorPersistence"/> class.</summary>
    /// <param name="state">The loaded runtime state whose <see cref="RuntimeState.RecentColors"/> list is owned here.</param>
    /// <param name="save">The atomic save delegate; returns <see langword="false"/> on a non-fatal failure.</param>
    /// <param name="timeProvider">Clock/timer source for the debounce; defaults to the system clock.</param>
    /// <param name="writeInterval">Debounce window; defaults to <see cref="DefaultWriteInterval"/>.</param>
    /// <param name="logger">Logger for non-fatal write failures; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> or <paramref name="save"/> is <see langword="null"/>.</exception>
    public RecentColorPersistence(
        RuntimeState state,
        Func<RuntimeState, CancellationToken, ValueTask<bool>> save,
        TimeProvider? timeProvider = null,
        TimeSpan? writeInterval = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(save);

        _state = state;
        _palette = new RecentColorPalette(state.RecentColors);
        _save = save;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _writeInterval = writeInterval ?? DefaultWriteInterval;
        _logger = (logger ?? Log.Logger).ForContext<RecentColorPersistence>();

        // Keep the runtime state coherent from the start (the tolerant loader may have normalized
        // the persisted list), without scheduling a write for a pure normalization.
        _state.RecentColors = _palette.HexColors.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ColorSwatch> Colors => _palette.Colors;

    /// <summary>Gets the current colours as normalized hex strings, most recent first.</summary>
    public IReadOnlyList<string> HexColors => _palette.HexColors;

    /// <inheritdoc />
    public void Record(string? hex)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var before = _palette.HexColors;
            _palette.Record(hex);
            var after = _palette.HexColors;
            if (before.SequenceEqual(after, StringComparer.Ordinal))
            {
                return;
            }

            _state.RecentColors = after.ToList();
            _dirty = true;
            ScheduleLocked();
        }
    }

    /// <summary>
    /// Cancels any pending debounce and, when there are unsaved changes, saves immediately (used by
    /// the shutdown participant). Failures are logged and returned as <see langword="false"/>; the
    /// change stays dirty for a later retry.
    /// </summary>
    /// <param name="cancellationToken">Cancels the save between the temp write and the replace.</param>
    /// <returns><see langword="true"/> when there was nothing to save or the save succeeded.</returns>
    public async ValueTask<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        ITimer? pending;
        lock (_gate)
        {
            pending = _timer;
            _timer = null;
        }

        pending?.Dispose();
        return await SaveNowAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void ScheduleLocked()
    {
        if (_disposed || _timer is not null)
        {
            return;
        }

        _timer = _timeProvider.CreateTimer(
            _ => OnDebounceElapsed(),
            state: null,
            dueTime: _writeInterval,
            period: Timeout.InfiniteTimeSpan);
    }

    private void OnDebounceElapsed()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }

        _ = SaveNowAsync(CancellationToken.None);
    }

    private async ValueTask<bool> SaveNowAsync(CancellationToken cancellationToken)
    {
        RuntimeState snapshot;
        lock (_gate)
        {
            if (!_dirty)
            {
                return true;
            }

            _dirty = false;

            // Copy every runtime flag: state.json holds one combined model, so a colour save must
            // never drop toggle states or the close-notification flag (and vice versa).
            snapshot = new RuntimeState
            {
                SchemaVersion = _state.SchemaVersion,
                CloseNotificationShown = _state.CloseNotificationShown,
                ToggleStates = new Dictionary<string, bool>(_state.ToggleStates, StringComparer.Ordinal),
                RecentColors = _palette.HexColors.ToList(),
            };
        }

        try
        {
            var saved = await _save(snapshot, cancellationToken).ConfigureAwait(false);
            if (!saved)
            {
                lock (_gate)
                {
                    _dirty = true;
                }

                _logger.Warning("Runtime state: recent colours could not be saved; retrying on the next change or shutdown");
            }

            return saved;
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _dirty = true;
            }

            _logger.Warning("Runtime state: persisting recent colours was canceled; the previous file was left unchanged");
            return false;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _dirty = true;
            }

            _logger.Warning(ex, "Runtime state: persisting recent colours failed unexpectedly; continuing without persistence");
            return false;
        }
    }
}
