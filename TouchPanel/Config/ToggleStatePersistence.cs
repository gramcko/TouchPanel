using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace TouchPanel.Config;

/// <summary>
/// Owns the optional persisted on/off state of Toggle widgets (RF-045/M2-RF-451..455). State is
/// kept in the existing <c>state.json</c> runtime store (never <c>panel.json</c>), keyed by
/// <c>"&lt;pageId&gt;/&lt;widgetId&gt;"</c>. Writes are debounced/coalesced (at most one atomic
/// save per interval and one final flush on shutdown) and are always failure-tolerant: a failed
/// save is logged and retried on the next change or shutdown. Stale keys are pruned against the
/// live configuration so the map does not grow without bound.
/// </summary>
/// <remarks>
/// The class is deliberately independent of the file system: it is constructed with the loaded
/// <see cref="RuntimeState"/> and a save delegate (the composition root passes
/// <see cref="StateStore.SaveAsync"/>), so the debounce/prune/reconcile semantics are unit-testable
/// with a fake clock and a counting save.
/// </remarks>
public sealed class ToggleStatePersistence : IDisposable
{
    /// <summary>
    /// Default debounce window: a flush happens at most once per this interval while changes keep
    /// arriving (M2-RF-453, "at most one write per few seconds").
    /// </summary>
    public static readonly TimeSpan DefaultWriteInterval = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly RuntimeState _state;
    private readonly Func<RuntimeState, CancellationToken, ValueTask<bool>> _save;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _writeInterval;
    private readonly ILogger _logger;
    private ITimer? _timer;
    private bool _dirty;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToggleStatePersistence"/> class.
    /// </summary>
    /// <param name="state">The loaded runtime state whose <see cref="RuntimeState.ToggleStates"/> map is owned here.</param>
    /// <param name="save">The atomic save delegate; returns <see langword="false"/> on a non-fatal failure.</param>
    /// <param name="timeProvider">Clock/timer source for the debounce; defaults to the system clock.</param>
    /// <param name="writeInterval">Debounce window; defaults to <see cref="DefaultWriteInterval"/>.</param>
    /// <param name="logger">Logger for non-fatal write failures; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> or <paramref name="save"/> is <see langword="null"/>.</exception>
    public ToggleStatePersistence(
        RuntimeState state,
        Func<RuntimeState, CancellationToken, ValueTask<bool>> save,
        TimeProvider? timeProvider = null,
        TimeSpan? writeInterval = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(save);

        _state = state;
        _save = save;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _writeInterval = writeInterval ?? DefaultWriteInterval;
        _logger = (logger ?? Log.Logger).ForContext<ToggleStatePersistence>();
    }

    /// <summary>
    /// Composes the persisted-state key for one widget (M2-RF-452): <c>"&lt;pageId&gt;/&lt;widgetId&gt;"</c>.
    /// </summary>
    /// <param name="pageId">The owning page id; must not be empty.</param>
    /// <param name="widgetId">The widget id; must not be empty.</param>
    /// <returns>The composite key used as the <c>toggleStates</c> map key.</returns>
    /// <exception cref="ArgumentException">Thrown when either id is empty.</exception>
    public static string ComposeKey(string pageId, string widgetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        return pageId + "/" + widgetId;
    }

    /// <summary>
    /// Collects the composite keys of every Toggle widget across all pages that opted into
    /// persistence (<see cref="ToggleWidgetConfig.PersistState"/>). Used to prune state left behind
    /// by pages/widgets that no longer exist (M2-RF-454).
    /// </summary>
    /// <param name="config">The effective configuration; must not be <see langword="null"/>.</param>
    /// <returns>The live persistent-toggle keys, in configuration order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<string> CollectPersistentToggleKeys(PanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var keys = new List<string>();
        foreach (var page in config.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Id))
            {
                continue;
            }

            foreach (var widget in page.Widgets)
            {
                if (widget is ToggleWidgetConfig { PersistState: true }
                    && !string.IsNullOrWhiteSpace(widget.Id))
                {
                    keys.Add(ComposeKey(page.Id, widget.Id));
                }
            }
        }

        return keys;
    }

    /// <summary>
    /// Looks up the persisted on/off state for one toggle, or <see langword="null"/> when no state
    /// was persisted (the caller then keeps <see cref="ToggleWidgetConfig.InitialOn"/>). An
    /// absent/empty map or a missing key is always non-fatal.
    /// </summary>
    /// <param name="pageId">The owning page id, or <see langword="null"/>.</param>
    /// <param name="widgetId">The widget id, or <see langword="null"/>.</param>
    /// <returns>The persisted state, or <see langword="null"/> when none is stored.</returns>
    public bool? TryGet(string? pageId, string? widgetId)
    {
        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(widgetId))
        {
            return null;
        }

        var key = ComposeKey(pageId, widgetId);
        lock (_gate)
        {
            return _state.ToggleStates.TryGetValue(key, out var isOn) ? isOn : null;
        }
    }

    /// <summary>
    /// Records a new on/off state for one toggle and schedules a debounced save. A no-op write
    /// (same value already stored) does not schedule anything; empty ids are ignored.
    /// </summary>
    /// <param name="pageId">The owning page id, or <see langword="null"/>.</param>
    /// <param name="widgetId">The widget id, or <see langword="null"/>.</param>
    /// <param name="isOn">The new on/off state.</param>
    public void Set(string? pageId, string? widgetId, bool isOn)
    {
        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(widgetId))
        {
            return;
        }

        var key = ComposeKey(pageId, widgetId);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_state.ToggleStates.TryGetValue(key, out var existing) && existing == isOn)
            {
                return;
            }

            _state.ToggleStates[key] = isOn;
            _dirty = true;
            ScheduleLocked();
        }
    }

    /// <summary>
    /// Removes any persisted key not present in <paramref name="liveKeys"/> and schedules a save when
    /// something was pruned (M2-RF-454).
    /// </summary>
    /// <param name="liveKeys">The composite keys still referenced by the configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="liveKeys"/> is <see langword="null"/>.</exception>
    public void Prune(IEnumerable<string> liveKeys)
    {
        ArgumentNullException.ThrowIfNull(liveKeys);
        var live = new HashSet<string>(liveKeys, StringComparer.Ordinal);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            List<string>? stale = null;
            foreach (var key in _state.ToggleStates.Keys)
            {
                if (!live.Contains(key))
                {
                    (stale ??= []).Add(key);
                }
            }

            if (stale is null)
            {
                return;
            }

            foreach (var key in stale)
            {
                _state.ToggleStates.Remove(key);
            }

            _dirty = true;
            ScheduleLocked();
        }
    }

    /// <summary>
    /// Cancels any pending debounce and, when there are unsaved changes, performs one atomic save
    /// immediately (used by the shutdown participant). Failures are logged and returned as
    /// <see langword="false"/>; the change stays dirty for a later retry.
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
            snapshot = new RuntimeState
            {
                SchemaVersion = _state.SchemaVersion,
                CloseNotificationShown = _state.CloseNotificationShown,
                ToggleStates = new Dictionary<string, bool>(_state.ToggleStates, StringComparer.Ordinal),
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

                _logger.Warning("Runtime state: persisted toggle states could not be saved; retrying on the next change or shutdown");
            }

            return saved;
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _dirty = true;
            }

            _logger.Warning("Runtime state: persisting toggle states was canceled; the previous file was left unchanged");
            return false;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _dirty = true;
            }

            _logger.Warning(ex, "Runtime state: persisting toggle states failed unexpectedly; continuing without persistence");
            return false;
        }
    }
}
