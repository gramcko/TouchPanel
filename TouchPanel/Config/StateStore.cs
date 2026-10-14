using System.IO;
using System.Text;
using System.Text.Json;
using Serilog;

namespace TouchPanel.Config;

/// <summary>
/// Reads and writes <c>state.json</c>, the non-user-facing runtime flag store (RF-073, D-12).
/// A missing, unreadable, corrupt, or stale-version file is non-fatal: <see cref="LoadAsync"/>
/// logs at low severity and regenerates valid defaults so the application can always continue.
/// Saves are atomic (temp file + durable flush + replace) and failures return
/// <see langword="false"/> instead of throwing.
/// </summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly ConfigPaths _paths;
    private readonly ILogger _logger;
    private readonly IAppWriteSuppression? _writeSuppression;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="StateStore"/> class.
    /// </summary>
    /// <param name="paths">Injected file paths; defaults to the real per-user profile (tests always inject a temp directory).</param>
    /// <param name="logger">Logger for low-severity resilience messages; defaults to the global Serilog logger.</param>
    /// <param name="writeSuppression">
    /// Optional self-write seam (M2-RF-823): when supplied, every <c>state.json</c> write opens a
    /// suppression window so the hot-reload decision component ignores the app's own runtime-state
    /// writes (toggle persistence). Defaults to <see langword="null"/>.
    /// </param>
    public StateStore(
        ConfigPaths? paths = null,
        ILogger? logger = null,
        IAppWriteSuppression? writeSuppression = null)
    {
        _paths = paths ?? ConfigPaths.Default;
        _logger = (logger ?? Serilog.Log.Logger).ForContext<StateStore>();
        _writeSuppression = writeSuppression;
    }

    /// <summary>
    /// Loads the runtime state, always returning a usable instance: a missing or corrupt file is
    /// regenerated with defaults (best effort) and reported at Info/Warning severity.
    /// </summary>
    /// <param name="cancellationToken">Cancels the file I/O.</param>
    /// <returns>The loaded state, or freshly generated defaults when the file was unusable.</returns>
    public async ValueTask<RuntimeState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_paths.StateFile))
            {
                _logger.Information("Runtime state file is missing; regenerating defaults");
                return await RegenerateAsync(cancellationToken).ConfigureAwait(false);
            }

            string text;
            try
            {
                text = await File.ReadAllTextAsync(_paths.StateFile, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Runtime state file could not be read; regenerating defaults");
                return await RegenerateAsync(cancellationToken).ConfigureAwait(false);
            }

            RuntimeState? state;
            try
            {
                state = JsonSerializer.Deserialize<RuntimeState>(text, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.Warning("Runtime state file is corrupt ({Reason}); regenerating defaults", ex.GetType().Name);
                return await RegenerateAsync(cancellationToken).ConfigureAwait(false);
            }

            if (state is null || state.SchemaVersion != RuntimeState.CurrentSchemaVersion)
            {
                _logger.Warning("Runtime state file is stale or empty; regenerating defaults");
                return await RegenerateAsync(cancellationToken).ConfigureAwait(false);
            }

            return state;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Atomically saves the runtime state. Failures are logged and returned as
    /// <see langword="false"/>; they never throw and never corrupt the previous file.
    /// </summary>
    /// <param name="state">The state to persist.</param>
    /// <param name="cancellationToken">Cancels the write between the temp write and the replace.</param>
    /// <returns><see langword="true"/> when the file was replaced successfully.</returns>
    public async ValueTask<bool> SaveAsync(RuntimeState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SaveCoreAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<RuntimeState> RegenerateAsync(CancellationToken cancellationToken)
    {
        var state = new RuntimeState();

        // Best effort: if even the regeneration cannot be written, the defaults are still usable
        // and the next save attempt will retry; runtime flags are disposable by design.
        await SaveCoreAsync(state, cancellationToken).ConfigureAwait(false);
        return state;
    }

    private async ValueTask<bool> SaveCoreAsync(RuntimeState state, CancellationToken cancellationToken)
    {
        var tempPath = _paths.StateFile + ".tmp";
        IAppWriteScope? writeScope = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_paths.DataDirectory);

            var json = JsonSerializer.Serialize(state, JsonOptions);

            // M2-RF-823: suppress app-originated state writes (toggle persistence) from the
            // hot-reload decision component. state.json is outside the watcher filter (M2-RF-821),
            // but the shared window keeps the contract uniform.
            writeScope = _writeSuppression?.BeginWrite();

            // Keep the durable flush and replace off the UI thread (RNF-004).
            await Task.Run(
                () =>
                {
                    WriteDurable(tempPath, json);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (File.Exists(_paths.StateFile))
                    {
                        File.Replace(tempPath, _paths.StateFile, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, _paths.StateFile);
                    }
                },
                CancellationToken.None).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            _logger.Warning("Runtime state save canceled; the previous file was left unchanged");
            return false;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            _logger.Warning(ex, "Runtime state could not be saved; continuing without persistence");
            return false;
        }
        finally
        {
            writeScope?.Dispose();
        }
    }

    private static void WriteDurable(string path, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Best effort only.
        }
    }
}
