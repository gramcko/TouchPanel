using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Serilog;
using Serilog.Events;
using TouchPanel.Config.Json;
using TouchPanel.Config.Migrations;
using TouchPanel.Config.Validation;
using TouchPanel.Services.Logging;
using TouchPanel.Services.Time;

namespace TouchPanel.Config;

/// <summary>
/// File-backed <see cref="IConfigStore"/> (DD-9, RF-073..RF-081): classifies <c>panel.json</c>
/// into the four RF-075 states, migrates older files through <see cref="MigrationRunner"/>,
/// validates entry-level problems through <see cref="ConfigValidator"/>, preserves corrupt and
/// future sources as <c>panel.corrupt-&lt;timestamp&gt;.json</c>, recovers from the last known
/// valid <c>panel.backup.json</c> or generated defaults, and saves atomically
/// (temp file + durable flush + <c>File.Replace</c> with backup rotation). A save whose effective
/// load came from an unsupported-future source is refused so that source stays intact (RF-075.4),
/// and a failed shutdown save can write a best-effort <c>panel.recovery.json</c> (F-08). It never
/// polls or watches the file system and owns a single writer gate (RF-080).
/// </summary>
public sealed class JsonConfigStore : IConfigStore
{
    private readonly ConfigPaths _paths;
    private readonly MigrationRunner _migrations;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly IAppWriteSuppression? _writeSuppression;
    private readonly IClockCultureSource? _clockCulture;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Whether the current <c>panel.json</c> content is a version this store validated (current or
    /// successfully migrated). Only then may it be rotated into <c>panel.backup.json</c>; corrupt
    /// or future sources must never replace a known-good backup (RF-078).
    /// </summary>
    private bool _panelFileIsRotatable;

    /// <summary>
    /// Whether the effective load originated from an unsupported-future <c>panel.json</c>
    /// (RF-075.4). Such a source must not be overwritten by a later save, even after the app has
    /// been running from the recovered backup or defaults; normal corrupt-recovery does not set
    /// this and stays saveable.
    /// </summary>
    private bool _protectedFutureSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConfigStore"/> class.
    /// </summary>
    /// <param name="paths">Injected file paths; defaults to the real per-user profile (tests always inject a temp directory).</param>
    /// <param name="migrations">The migration chain; defaults to the production v1 baseline (no steps).</param>
    /// <param name="timeProvider">Clock used for preservation timestamps; defaults to the system clock.</param>
    /// <param name="logger">Logger for load/save/migration outcomes; defaults to the global Serilog logger.</param>
    /// <param name="writeSuppression">
    /// Optional self-write seam (M2-RF-823): when supplied, every <c>panel.json</c> write opens a
    /// suppression window so the hot-reload watcher ignores the app's own atomic save. Defaults to
    /// <see langword="null"/>, which preserves the pre-hot-reload behavior.
    /// </param>
    /// <param name="clockCulture">
    /// The canonical Clock culture source (M4-RF-951) supplied to load-time validation so a custom
    /// Clock format is checked against the same display culture the Clock view model uses. When
    /// <see langword="null"/>, <see cref="ClockCultureSource.Default"/> is used and the default
    /// migration chain is built with the same source.
    /// </param>
    public JsonConfigStore(
        ConfigPaths? paths = null,
        MigrationRunner? migrations = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        IAppWriteSuppression? writeSuppression = null,
        IClockCultureSource? clockCulture = null)
    {
        _paths = paths ?? ConfigPaths.Default;
        _clockCulture = clockCulture;
        _migrations = migrations ?? new MigrationRunner(ConfigMigrationChain.Create(), clockCulture: clockCulture);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = (logger ?? Serilog.Log.Logger).ForContext<JsonConfigStore>();
        _writeSuppression = writeSuppression;
    }

    /// <inheritdoc />
    public async ValueTask<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<ConfigSaveResult> SaveAsync(
        PanelConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown paths cancel the save budget; report that as a retryable failure instead
            // of throwing so shutdown can finish.
            return ConfigSaveResult.Failure(
                "config.save.canceled",
                "The save was canceled; the previous configuration was left unchanged.");
        }

        try
        {
            return await SaveCoreAsync(config, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryWriteRecoveryCopyAsync(
        PanelConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.SchemaVersion != PanelConfig.CurrentSchemaVersion)
        {
            _logger.Error(
                "Configuration recovery copy refused: the in-memory schema version {SchemaVersion} is not the supported version {SupportedVersion}",
                config.SchemaVersion,
                PanelConfig.CurrentSchemaVersion);
            return false;
        }

        var tempPath = _paths.RecoveryFile + ".tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_paths.DataDirectory);
            var json = JsonSerializer.Serialize(config, ConfigJson.Options);

            // Same durable write + atomic move as a normal save (RF-077), against a distinct
            // target and temp file, so panel.json and panel.backup.json are never touched (F-08).
            await Task.Run(
                () =>
                {
                    WriteDurable(tempPath, json);
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(tempPath, _paths.RecoveryFile, overwrite: true);
                },
                CancellationToken.None).ConfigureAwait(false);

            _logger.Information("Configuration recovery copy written: {RecoveryFile}", _paths.RecoveryFile);
            return true;
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            _logger.Warning("Configuration recovery copy canceled; shutdown is not blocked");
            return false;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            _logger.Error(ex, "Configuration recovery copy failed: {RecoveryFile}", _paths.RecoveryFile);
            return false;
        }
    }

    private async ValueTask<ConfigLoadResult> LoadCoreAsync(CancellationToken cancellationToken)
    {
        // Each load reclassifies the file from scratch; the protection flag never leaks between
        // loads on the same store instance (RF-080 keeps a single writer but not a single load).
        _protectedFutureSource = false;

        try
        {
            Directory.CreateDirectory(_paths.DataDirectory);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Configuration directory is unavailable: {Directory}", _paths.DataDirectory);
            return InMemoryDefaults(
                ConfigState.Invalid,
                detectedVersion: null,
                [ConfigDiagnostic.Error(
                    "config.directory.failed",
                    "The configuration directory could not be prepared; generated defaults are in use.")]);
        }

        if (!File.Exists(_paths.PanelFile))
        {
            return await FirstRunAsync(cancellationToken).ConfigureAwait(false);
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(_paths.PanelFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Configuration file could not be read: {File}", _paths.PanelFile);
            return await RecoverAsync(
                ConfigState.Invalid,
                detectedVersion: null,
                [ConfigDiagnostic.Error("config.read.failed", "The configuration file could not be read; recovered values are in use.")],
                cancellationToken).ConfigureAwait(false);
        }

        if (!TryProbe(text, out var version, out var problemCode, out var problemMessage))
        {
            return await RecoverAsync(
                ConfigState.Invalid,
                detectedVersion: null,
                [ConfigDiagnostic.Error(problemCode, problemMessage)],
                cancellationToken).ConfigureAwait(false);
        }

        if (version > PanelConfig.CurrentSchemaVersion)
        {
            // RF-075.4: do not interpret and do not overwrite; preserve for diagnosis and run
            // from the backup or defaults.
            return await RecoverAsync(
                ConfigState.UnsupportedFuture,
                version,
                [ConfigDiagnostic.Error(
                    "config.schema.future",
                    $"Schema version {version} is newer than the supported version {PanelConfig.CurrentSchemaVersion}; the file was not interpreted.")],
                cancellationToken).ConfigureAwait(false);
        }

        PanelConfig? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PanelConfig>(text, ConfigJson.Options);
        }
        catch (JsonException ex)
        {
            return await RecoverAsync(
                ConfigState.Invalid,
                version,
                [ConfigDiagnostic.Error(
                    "config.structure.invalid",
                    LogRedactor.RedactText($"The configuration is structurally invalid ({ex.GetType().Name}: {ex.Message})."))],
                cancellationToken).ConfigureAwait(false);
        }

        if (parsed is null)
        {
            return await RecoverAsync(
                ConfigState.Invalid,
                version,
                [ConfigDiagnostic.Error("config.structure.invalid", "The configuration deserialized to no value.")],
                cancellationToken).ConfigureAwait(false);
        }

        if (version == PanelConfig.CurrentSchemaVersion)
        {
            // M2-RF-823/D2: seed the reload baseline with the content that was loaded and is now
            // running, so an external write of byte-identical content is a no-op from the first
            // event even on a cold start where this store has not saved panel.json yet. This does
            // not open a self-write window (no suppression), so external events still debounce.
            _writeSuppression?.RecordAppliedContent(text);

            var diagnostics = ConfigValidator.Validate(parsed, _clockCulture);
            _panelFileIsRotatable = true;
            LogLoad(ConfigState.Current, version, diagnostics);
            return new ConfigLoadResult
            {
                State = ConfigState.Current,
                Config = parsed,
                DetectedSchemaVersion = version,
                Diagnostics = diagnostics,
            };
        }

        return await MigrateAndSaveAsync(parsed, version, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>RF-075.1: first run, no file at all; generate and persist defaults.</summary>
    private async ValueTask<ConfigLoadResult> FirstRunAsync(CancellationToken cancellationToken)
    {
        var config = DefaultConfigGenerator.Create();
        var diagnostics = new List<ConfigDiagnostic>
        {
            ConfigDiagnostic.Info("config.missing", "No configuration file was found; generated defaults are in use."),
        };
        diagnostics.AddRange(ConfigValidator.Validate(config, _clockCulture));

        var save = await SaveCoreAsync(config, cancellationToken).ConfigureAwait(false);
        diagnostics.AddRange(save.Diagnostics);
        if (save.Succeeded)
        {
            diagnostics.Add(ConfigDiagnostic.Info("config.defaults.saved", "The default configuration was saved."));
        }

        LogLoad(ConfigState.Current, detectedVersion: null, diagnostics);
        return new ConfigLoadResult
        {
            State = ConfigState.Current,
            Config = config,
            DetectedSchemaVersion = null,
            Diagnostics = diagnostics,
        };
    }

    /// <summary>
    /// RF-075.2: migrate an older supported file, validate the result, back the original up, and
    /// save the migrated file. The original is only replaced after the migrated configuration was
    /// validated and written to the temp file (RF-076).
    /// </summary>
    private async ValueTask<ConfigLoadResult> MigrateAndSaveAsync(
        PanelConfig parsed,
        int version,
        CancellationToken cancellationToken)
    {
        MigrationResult migration;
        try
        {
            migration = _migrations.Migrate(parsed, version);
        }
        catch (Exception ex)
        {
            var message = LogRedactor.RedactText(
                $"Migration from schema {version} failed ({ex.GetType().Name}: {ex.Message}); the source file was preserved.");
            _logger.Error("Configuration migration failed: {Message}", message);
            return await RecoverAsync(
                ConfigState.Invalid,
                version,
                [ConfigDiagnostic.Error("config.migration.failed", message)],
                cancellationToken).ConfigureAwait(false);
        }

        _panelFileIsRotatable = true;

        // Save rotates the original older file into panel.backup.json atomically while replacing
        // panel.json with the migrated configuration (RF-075.2, RF-078).
        var save = await SaveCoreAsync(migration.Config, cancellationToken).ConfigureAwait(false);
        var diagnostics = new List<ConfigDiagnostic>(migration.Diagnostics);
        diagnostics.AddRange(save.Diagnostics);
        if (save.Succeeded)
        {
            diagnostics.Add(ConfigDiagnostic.Info(
                "config.migrated.saved",
                $"Migrated configuration saved (schema {version} → {PanelConfig.CurrentSchemaVersion}); the original was rotated to the backup."));
        }

        LogLoad(ConfigState.OlderSupported, version, diagnostics);
        return new ConfigLoadResult
        {
            State = ConfigState.OlderSupported,
            Config = migration.Config,
            DetectedSchemaVersion = version,
            Diagnostics = diagnostics,
        };
    }

    /// <summary>
    /// RF-075.3/.4: preserve the unreadable, corrupt, or future source for diagnosis, then recover
    /// from the last known valid backup or generated defaults. <c>panel.json</c> is never
    /// overwritten here and never becomes the backup.
    /// </summary>
    private async ValueTask<ConfigLoadResult> RecoverAsync(
        ConfigState state,
        int? detectedVersion,
        IReadOnlyList<ConfigDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var all = new List<ConfigDiagnostic>(diagnostics);

        TryPreserveSource(all);

        var recovered = await TryLoadBackupAsync(all, cancellationToken).ConfigureAwait(false);
        if (recovered is not null)
        {
            all.Add(ConfigDiagnostic.Info(
                "config.recovered.backup",
                "Recovered the last known valid configuration from panel.backup.json."));
        }
        else
        {
            recovered = DefaultConfigGenerator.Create();
            all.AddRange(ConfigValidator.Validate(recovered, _clockCulture));
            all.Add(ConfigDiagnostic.Warning(
                "config.recovered.defaults",
                "No usable backup was found; generated defaults are in use."));
        }

        _panelFileIsRotatable = false;

        // RF-075.4: the effective configuration may come from the backup or defaults, but the
        // future-version panel.json is the source on disk and stays protected from later saves.
        _protectedFutureSource = state == ConfigState.UnsupportedFuture;

        LogLoad(state, detectedVersion, all);
        return new ConfigLoadResult
        {
            State = state,
            Config = recovered,
            DetectedSchemaVersion = detectedVersion,
            Diagnostics = all,
        };
    }

    private ConfigLoadResult InMemoryDefaults(
        ConfigState state,
        int? detectedVersion,
        List<ConfigDiagnostic> diagnostics)
    {
        var config = DefaultConfigGenerator.Create();
        diagnostics.AddRange(ConfigValidator.Validate(config, _clockCulture));
        _panelFileIsRotatable = false;
        LogLoad(state, detectedVersion, diagnostics);
        return new ConfigLoadResult
        {
            State = state,
            Config = config,
            DetectedSchemaVersion = detectedVersion,
            Diagnostics = diagnostics,
        };
    }

    private void TryPreserveSource(List<ConfigDiagnostic> diagnostics)
    {
        try
        {
            if (!File.Exists(_paths.PanelFile))
            {
                return;
            }

            var target = GetUniquePreservationPath(_timeProvider.GetUtcNow());
            File.Copy(_paths.PanelFile, target, overwrite: false);
            _logger.Warning("Configuration source preserved for diagnosis: {PreservedFile}", target);
            diagnostics.Add(ConfigDiagnostic.Info(
                "config.source.preserved",
                $"The source file was preserved as '{Path.GetFileName(target)}'."));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Configuration source could not be preserved");
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.source.preserveFailed",
                $"The source file could not be preserved ({ex.GetType().Name})."));
        }
    }

    private string GetUniquePreservationPath(DateTimeOffset timestamp)
    {
        var candidate = _paths.GetCorruptFilePath(timestamp);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(candidate);
        var index = 1;
        while (true)
        {
            var suffixed = Path.Combine(
                _paths.DataDirectory,
                baseName + "-" + index.ToString(CultureInfo.InvariantCulture) + ".json");
            if (!File.Exists(suffixed))
            {
                return suffixed;
            }

            index++;
        }
    }

    private async ValueTask<PanelConfig?> TryLoadBackupAsync(
        List<ConfigDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.BackupFile))
        {
            return null;
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(_paths.BackupFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.backup.invalid",
                $"The backup could not be read ({ex.GetType().Name})."));
            return null;
        }

        if (!TryProbe(text, out var version, out _, out _) || version > PanelConfig.CurrentSchemaVersion)
        {
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.backup.invalid",
                "The backup is not a usable configuration and was ignored."));
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<PanelConfig>(text, ConfigJson.Options);
            if (parsed is null)
            {
                diagnostics.Add(ConfigDiagnostic.Warning(
                    "config.backup.invalid",
                    "The backup deserialized to no value and was ignored."));
                return null;
            }

            if (version == PanelConfig.CurrentSchemaVersion)
            {
                diagnostics.AddRange(ConfigValidator.Validate(parsed, _clockCulture));
                return parsed;
            }

            var migration = _migrations.Migrate(parsed, version);
            diagnostics.AddRange(migration.Diagnostics);
            return migration.Config;
        }
        catch (Exception ex)
        {
            diagnostics.Add(ConfigDiagnostic.Warning(
                "config.backup.invalid",
                $"The backup is not a usable configuration ({ex.GetType().Name})."));
            return null;
        }
    }

    private async ValueTask<ConfigSaveResult> SaveCoreAsync(
        PanelConfig config,
        CancellationToken cancellationToken)
    {
        if (_protectedFutureSource)
        {
            // RF-075.4/F-05: the effective run state came from a future-version panel.json; that
            // file must stay intact, so later saves are refused instead of replacing it.
            var protectedMessage =
                "Refusing to save because panel.json was created by a newer schema version and must not be overwritten.";
            _logger.Error("Configuration save refused: {Message}", protectedMessage);
            return ConfigSaveResult.Failure("config.save.protectedFutureSource", protectedMessage);
        }

        if (config.SchemaVersion != PanelConfig.CurrentSchemaVersion)
        {
            // Never write a file this build does not own; refusing keeps the previous file intact.
            var versionMessage =
                $"Refusing to save schema version {config.SchemaVersion}; this build writes schema version {PanelConfig.CurrentSchemaVersion}.";
            _logger.Error("Configuration save refused: {Message}", versionMessage);
            return ConfigSaveResult.Failure("config.save.version", versionMessage);
        }

        var tempPath = _paths.PanelFile + ".tmp";
        var rotateBackup = _panelFileIsRotatable;
        IAppWriteScope? writeScope = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_paths.DataDirectory);

            var json = JsonSerializer.Serialize(config, ConfigJson.Options);

            // M2-RF-823: open the self-write window around the atomic save so the hot-reload
            // watcher ignores the temp-file/replace events the store itself generates.
            writeScope = _writeSuppression?.BeginWrite();

            // The durable flush and the atomic replace run on the thread pool so the WPF UI
            // thread is never blocked by configuration I/O (RNF-004).
            await Task.Run(
                () =>
                {
                    WriteDurable(tempPath, json);
                    cancellationToken.ThrowIfCancellationRequested();
                    ReplaceOrMove(tempPath, rotateBackup);
                },
                CancellationToken.None).ConfigureAwait(false);

            // Contribute the exact bytes so an event with identical content is a no-op by hash.
            writeScope?.RecordContent(json);

            _panelFileIsRotatable = true;
            _logger.Information(
                "Configuration saved. SchemaVersion={SchemaVersion} PreviousFileRotatedToBackup={BackupRotated}",
                config.SchemaVersion,
                rotateBackup);
            return ConfigSaveResult.Success();
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            _logger.Warning("Configuration save canceled; the previous file was left unchanged");
            return ConfigSaveResult.Failure(
                "config.save.canceled",
                "The save was canceled; the previous configuration was left unchanged.");
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            var message = LogRedactor.RedactText(
                $"The configuration could not be saved ({ex.GetType().Name}: {ex.Message}); "
                + "the previous file was left unchanged and the save can be retried.");
            _logger.Error(ex, "Configuration save failed: {Message}", message);
            return ConfigSaveResult.Failure("config.save.failed", message);
        }
        finally
        {
            writeScope?.Dispose();
        }
    }

    private void ReplaceOrMove(string tempPath, bool rotateBackup)
    {
        if (File.Exists(_paths.PanelFile))
        {
            // File.Replace atomically rotates the previous file into the backup and replaces the
            // destination (RF-077/RF-078). A corrupt or future source never becomes the backup.
            File.Replace(
                tempPath,
                _paths.PanelFile,
                rotateBackup ? _paths.BackupFile : null,
                ignoreMetadataErrors: true);
            return;
        }

        // First save: no destination exists yet, so File.Replace is not available (RF-077).
        File.Move(tempPath, _paths.PanelFile);
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
            // Best effort: a leftover temp file is harmless and overwritten by the next save.
        }
    }

    /// <summary>
    /// Light structural probe (file-level tier, RF-079): JSON syntax, root kind, and the presence
    /// of an integer <c>schemaVersion</c>. Typed deserialization happens only after this passes so
    /// a missing/unsupported version never reaches the converters.
    /// </summary>
    private static bool TryProbe(
        string text,
        out int version,
        out string problemCode,
        out string problemMessage)
    {
        version = 0;
        problemCode = string.Empty;
        problemMessage = string.Empty;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            problemCode = "config.parse.failed";
            problemMessage = LogRedactor.RedactText(
                $"The configuration is not valid JSON ({ex.GetType().Name}: {ex.Message}).");
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problemCode = "config.root.invalid";
                problemMessage = "The configuration root must be a JSON object.";
                return false;
            }

            if (!root.TryGetProperty("schemaVersion", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out version))
            {
                problemCode = "config.schema.missing";
                problemMessage = "The configuration must contain an integer 'schemaVersion'.";
                return false;
            }
        }

        return true;
    }

    private void LogLoad(ConfigState state, int? detectedVersion, IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        var errors = diagnostics.Count(d => d.Severity == ConfigDiagnosticSeverity.Error);
        var warnings = diagnostics.Count(d => d.Severity == ConfigDiagnosticSeverity.Warning);
        var level = state is ConfigState.Invalid or ConfigState.UnsupportedFuture
            ? LogEventLevel.Warning
            : LogEventLevel.Information;

        _logger.Write(
            level,
            "Configuration load finished. State={State} DetectedSchemaVersion={SchemaVersion} Errors={Errors} Warnings={Warnings}",
            state,
            detectedVersion,
            errors,
            warnings);

        foreach (var diagnostic in diagnostics)
        {
            switch (diagnostic.Severity)
            {
                case ConfigDiagnosticSeverity.Error:
                    _logger.Error("Configuration diagnostic {Code}: {Message}", diagnostic.Code, diagnostic.Message);
                    break;
                case ConfigDiagnosticSeverity.Warning:
                    _logger.Warning("Configuration diagnostic {Code}: {Message}", diagnostic.Code, diagnostic.Message);
                    break;
                default:
                    _logger.Debug("Configuration diagnostic {Code}: {Message}", diagnostic.Code, diagnostic.Message);
                    break;
            }
        }
    }
}
