using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Serilog;
using TouchPanel.Config.Json;
using TouchPanel.Services.Config;
using TouchPanel.Services.Notifications;

namespace TouchPanel.Config.ImportExport;

/// <summary>
/// Outcome of an export attempt (M2-RF-831/M2-RF-835). A failed export never leaves a partial file
/// behind and changes nothing in the running configuration.
/// </summary>
public sealed record ConfigExportResult
{
    /// <summary>Gets a value indicating whether the configuration was written to <see cref="Path"/>.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Gets the target path the export was directed at.</summary>
    public required string Path { get; init; }

    /// <summary>Gets a stable machine-readable failure code, or <see langword="null"/> on success.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Gets a human-readable failure description (never configuration content), or <see langword="null"/> on success.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Outcome of an import attempt (M2-RF-832/M2-RF-833). <see cref="Succeeded"/> is only
/// <see langword="true"/> when the chosen file validated as a whole, a pre-import backup was
/// written, the configuration persisted atomically, and the shared apply pipeline ran. Every other
/// outcome leaves the running configuration untouched and reports <see cref="Diagnostics"/>.
/// </summary>
public sealed record ConfigImportResult
{
    /// <summary>Gets a value indicating whether the imported configuration was applied.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Gets the source path the import was directed at.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the applied configuration when <see cref="Succeeded"/> is <see langword="true"/>; otherwise <see langword="null"/>.</summary>
    public PanelConfig? Config { get; init; }

    /// <summary>Gets the path of the pre-import backup, when one was written; otherwise <see langword="null"/>.</summary>
    public string? BackupPath { get; init; }

    /// <summary>Gets a stable machine-readable failure code, or <see langword="null"/> on success.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Gets a human-readable failure description (never configuration content), or <see langword="null"/> on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets the parse/migration/validation diagnostics gathered while importing; never <see langword="null"/>.</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// User-triggered configuration import/export (RF-083, M2-RF-831..835, MD-11). Export serializes the
/// current effective <see cref="PanelConfig"/> — including its <c>schemaVersion</c> — to a chosen
/// path. Import reads a chosen file, validates it as a whole (parse → migrate older supported →
/// validate) <em>before</em> touching anything, writes a timestamped pre-import backup of the
/// current configuration, persists the imported configuration through the existing single-writer
/// <see cref="IConfigStore"/> (atomic temp + replace), and finally applies it through the shared
/// <see cref="PanelConfigApplier"/> (the same pipeline hot reload uses, M2-RF-824). A failed import
/// changes nothing and reports diagnostics.
/// </summary>
/// <remarks>
/// M2 has no secret-bearing configuration fields (HTTP was dropped), so no redaction is required;
/// the general RF-085 rule still applies and this service only ever logs file paths and result
/// summaries, never configuration content (M2-RF-834). Imported content is treated as untrusted
/// data (RF-081): it goes through the same validator as any other file. All file I/O is async so no
/// UI thread is blocked (M2-RF-835); a file picker is supplied by the settings UI (T-17), not here.
/// </remarks>
public sealed class ConfigImportExport
{
    private readonly ConfigPaths _paths;
    private readonly PanelConfigCandidateReader _reader;
    private readonly IConfigStore _store;
    private readonly PanelConfigApplier _applier;
    private readonly Func<PanelConfig?> _configAccessor;
    private readonly Action<PanelConfig> _configSetter;
    private readonly IUserNotifier _notifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigImportExport"/> class.
    /// </summary>
    /// <param name="store">The existing single-writer store used to persist an imported configuration (RF-080/RF-077).</param>
    /// <param name="applier">The shared apply pipeline used after a successful import (MD-10/M2-RF-824).</param>
    /// <param name="configAccessor">Returns the currently running configuration; used to write the pre-import backup and to export.</param>
    /// <param name="configSetter">Atomically publishes the imported configuration as the running one.</param>
    /// <param name="notifier">The user-notification boundary (RF-010).</param>
    /// <param name="paths">Injected file paths; defaults to the real per-user profile (tests inject a temp directory).</param>
    /// <param name="reader">The whole-file candidate reader; defaults to the production reader over <paramref name="paths"/>.</param>
    /// <param name="timeProvider">Clock used for the pre-import backup timestamp; defaults to the system clock.</param>
    /// <param name="logger">Logger for path/summary-only messages; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public ConfigImportExport(
        IConfigStore store,
        PanelConfigApplier applier,
        Func<PanelConfig?> configAccessor,
        Action<PanelConfig> configSetter,
        IUserNotifier notifier,
        ConfigPaths? paths = null,
        PanelConfigCandidateReader? reader = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(configAccessor);
        ArgumentNullException.ThrowIfNull(configSetter);
        ArgumentNullException.ThrowIfNull(notifier);

        _store = store;
        _applier = applier;
        _configAccessor = configAccessor;
        _configSetter = configSetter;
        _notifier = notifier;
        _paths = paths ?? ConfigPaths.Default;
        _reader = reader ?? new PanelConfigCandidateReader(_paths, logger: logger);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = (logger ?? Log.Logger).ForContext<ConfigImportExport>();
    }

    /// <summary>
    /// Exports the current effective configuration as indented JSON (including its
    /// <c>schemaVersion</c>) to <paramref name="targetPath"/> (M2-RF-831). The write goes to a
    /// sibling temp file and is moved into place, so a failure never leaves a partial export. Only
    /// the path is logged; configuration content is never logged (M2-RF-834).
    /// </summary>
    /// <param name="targetPath">The full path to write the exported configuration to.</param>
    /// <param name="cancellationToken">Cancels the write before the atomic move.</param>
    /// <returns>The export result.</returns>
    public async ValueTask<ConfigExportResult> ExportAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = targetPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return ExportFailure(
                normalizedPath,
                "config.export.pathInvalid",
                "No export path was provided.");
        }

        var config = _configAccessor();
        if (config is null)
        {
            return ExportFailure(
                normalizedPath,
                "config.export.noConfig",
                "No configuration is available to export.");
        }

        string json;
        try
        {
            json = JsonSerializer.Serialize(config, ConfigJson.Options);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Configuration export: serialization failed. Path={Path}", normalizedPath);
            return ExportFailure(
                normalizedPath,
                "config.export.serializeFailed",
                "The configuration could not be serialized for export.");
        }

        var tempPath = normalizedPath + ".tmp";
        try
        {
            // Write the same bytes a save would, to a sibling temp file, then move atomically so a
            // failure cannot leave a truncated export (M2-RF-835).
            await File.WriteAllTextAsync(
                tempPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, normalizedPath, overwrite: true);

            _logger.Information("Configuration exported. Path={Path}", normalizedPath);
            _notifier.Notify(
                "TouchPanel",
                $"The configuration was exported to '{normalizedPath}'.",
                NotificationSeverity.Info);

            return new ConfigExportResult { Succeeded = true, Path = normalizedPath };
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            _logger.Warning("Configuration export was canceled; no file was written. Path={Path}", normalizedPath);
            return ExportFailure(
                normalizedPath,
                "config.export.canceled",
                "The export was canceled; no file was written.");
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            _logger.Error(ex, "Configuration export failed. Path={Path}", normalizedPath);
            return ExportFailure(
                normalizedPath,
                "config.export.failed",
                "The configuration could not be written to the selected location.");
        }
    }

    /// <summary>
    /// Imports the configuration at <paramref name="sourcePath"/> (M2-RF-832/M2-RF-833): reads and
    /// validates the whole file (parse → migrate older supported → validate) before changing
    /// anything, writes a timestamped pre-import backup of the current configuration, persists the
    /// imported configuration atomically through the store, and applies it through the shared apply
    /// pipeline. Any failure leaves the running configuration untouched and reports diagnostics;
    /// only paths and summaries are logged (M2-RF-834).
    /// </summary>
    /// <param name="sourcePath">The full path of the configuration file to import.</param>
    /// <param name="cancellationToken">Cancels the read/backup/save.</param>
    /// <returns>The import result.</returns>
    public async ValueTask<ConfigImportResult> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = sourcePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return ImportFailure(
                normalizedPath,
                "config.import.pathInvalid",
                "No import path was provided.");
        }

        PanelConfigCandidate candidate;
        try
        {
            candidate = await _reader.ReadFileAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ImportFailure(normalizedPath, "config.import.canceled", "The import was canceled; nothing changed.");
        }
        catch (Exception ex)
        {
            // The reader already contains operational failures, but a composition bug must not take
            // the caller down (RF-012).
            _logger.Error(ex, "Configuration import: reading the selected file failed. Path={Path}", normalizedPath);
            return ImportFailure(
                normalizedPath,
                "config.import.readFailed",
                "The selected configuration could not be read; nothing changed.");
        }

        if (!candidate.IsValid || candidate.Config is null)
        {
            ReportRejected(candidate, normalizedPath);
            return new ConfigImportResult
            {
                Succeeded = false,
                Path = normalizedPath,
                Diagnostics = candidate.Diagnostics,
                ErrorCode = ErrorCodeFor(candidate.Outcome),
                ErrorMessage = ErrorMessageFor(candidate.Outcome),
            };
        }

        var imported = candidate.Config;

        try
        {
            // Back up the current effective configuration before anything is replaced (M2-RF-832).
            string? backupPath = null;
            var current = _configAccessor();
            if (current is not null)
            {
                backupPath = await WritePreImportBackupAsync(current, cancellationToken).ConfigureAwait(false);
            }

            // Persist atomically through the existing single-writer store (RF-077/RF-080). A failed
            // save leaves the previous file intact, so nothing is applied.
            var save = await _store.SaveAsync(imported, cancellationToken).ConfigureAwait(false);
            if (!save.Succeeded)
            {
                _logger.Error(
                    "Configuration import: the imported configuration could not be saved; the running configuration is kept. Path={Path}",
                    normalizedPath);
                _notifier.Notify(
                    "TouchPanel",
                    "The imported configuration could not be saved; TouchPanel keeps the current configuration.",
                    NotificationSeverity.Error);
                return new ConfigImportResult
                {
                    Succeeded = false,
                    Path = normalizedPath,
                    BackupPath = backupPath,
                    Diagnostics = save.Diagnostics,
                    ErrorCode = "config.import.saveFailed",
                    ErrorMessage = "The imported configuration could not be saved; nothing changed.",
                };
            }

            // Publish as the running configuration, then apply through the shared pipeline
            // (M2-RF-833/M2-RF-824). Step failures are isolated inside the applier.
            _configSetter(imported);
            var failures = _applier.Apply(imported);

            _logger.Information(
                "Configuration imported and applied. Path={Path} BackupPath={BackupPath} StepFailures={StepFailures}",
                normalizedPath,
                backupPath ?? "(none)",
                failures);
            _notifier.Notify(
                "TouchPanel",
                $"The configuration was imported from '{normalizedPath}'.",
                NotificationSeverity.Info);

            return new ConfigImportResult
            {
                Succeeded = true,
                Path = normalizedPath,
                Config = imported,
                BackupPath = backupPath,
                Diagnostics = candidate.Diagnostics,
            };
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Configuration import was canceled; the running configuration is kept. Path={Path}", normalizedPath);
            return ImportFailure(normalizedPath, "config.import.canceled", "The import was canceled; nothing changed.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Configuration import: the backup/save/apply phase failed unexpectedly. Path={Path}", normalizedPath);
            _notifier.Notify(
                "TouchPanel",
                "The imported configuration could not be applied; TouchPanel keeps the current configuration.",
                NotificationSeverity.Error);
            return ImportFailure(
                normalizedPath,
                "config.import.failed",
                "The imported configuration could not be applied; nothing changed.");
        }
    }

    private async ValueTask<string> WritePreImportBackupAsync(
        PanelConfig current,
        CancellationToken cancellationToken)
    {
        var backupPath = GetUniqueBackupPath(_timeProvider.GetUtcNow());
        var json = JsonSerializer.Serialize(current, ConfigJson.Options);
        var tempPath = backupPath + ".tmp";

        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, backupPath, overwrite: false);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        _logger.Information("Configuration import: pre-import backup written. BackupPath={BackupPath}", backupPath);
        return backupPath;
    }

    private string GetUniqueBackupPath(DateTimeOffset timestamp)
    {
        var candidate = _paths.GetImportBackupFilePath(timestamp);
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

    private void ReportRejected(PanelConfigCandidate candidate, string sourcePath)
    {
        foreach (var diagnostic in candidate.Diagnostics)
        {
            var level = diagnostic.Severity switch
            {
                ConfigDiagnosticSeverity.Error => Serilog.Events.LogEventLevel.Error,
                ConfigDiagnosticSeverity.Warning => Serilog.Events.LogEventLevel.Warning,
                _ => Serilog.Events.LogEventLevel.Information,
            };

            _logger.Write(
                level,
                "Configuration import rejected the selected file {Code}: {Message} Path={Path}",
                diagnostic.Code,
                diagnostic.Message,
                sourcePath);
        }

        _logger.Warning(
            "Configuration import rejected the selected file; the running configuration is kept. Outcome={Outcome} Path={Path}",
            candidate.Outcome,
            sourcePath);

        _notifier.Notify(
            "TouchPanel",
            candidate.Outcome == PanelConfigCandidateOutcome.UnsupportedFuture
                ? "The selected configuration was created by a newer version of TouchPanel and was not imported."
                : "The selected configuration is not valid and was not imported; the current configuration is kept.",
            NotificationSeverity.Warning);
    }

    private static string ErrorCodeFor(PanelConfigCandidateOutcome outcome) => outcome switch
    {
        PanelConfigCandidateOutcome.Missing or PanelConfigCandidateOutcome.ReadError => "config.import.readFailed",
        PanelConfigCandidateOutcome.UnsupportedFuture => "config.import.futureSchema",
        _ => "config.import.invalid",
    };

    private static string ErrorMessageFor(PanelConfigCandidateOutcome outcome) => outcome switch
    {
        PanelConfigCandidateOutcome.Missing or PanelConfigCandidateOutcome.ReadError =>
            "The selected configuration could not be read; nothing changed.",
        PanelConfigCandidateOutcome.UnsupportedFuture =>
            "The selected configuration was created by a newer version of TouchPanel and was not imported.",
        _ => "The selected configuration is not valid and was not imported; nothing changed.",
    };

    private static ConfigExportResult ExportFailure(string path, string code, string message) =>
        new() { Succeeded = false, Path = path, ErrorCode = code, ErrorMessage = message };

    private static ConfigImportResult ImportFailure(string path, string code, string message) =>
        new() { Succeeded = false, Path = path, ErrorCode = code, ErrorMessage = message };

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
            // Best effort: a leftover temp file is harmless and overwritten by the next attempt.
        }
    }
}
