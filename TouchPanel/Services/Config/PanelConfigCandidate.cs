using System.IO;
using System.Text.Json;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Migrations;
using TouchPanel.Config.Validation;
using TouchPanel.Services.Logging;
using TouchPanel.Services.Time;

namespace TouchPanel.Services.Config;

/// <summary>
/// Classification of an external <c>panel.json</c> candidate read by the hot-reload pipeline
/// (M2-RF-822/M2-RF-825/M2-RF-826). Only <see cref="Valid"/> may be applied; every other value keeps
/// the currently running configuration and never writes the watched file.
/// </summary>
public enum PanelConfigCandidateOutcome
{
    /// <summary>The candidate was read, migrated if needed, and fully validated; it may be applied.</summary>
    Valid,

    /// <summary>The candidate is not valid JSON, has no usable schema version, or failed validation/migration.</summary>
    Invalid,

    /// <summary>The candidate declares a schema newer than this build understands; it is not interpreted and not written (RF-075.4).</summary>
    UnsupportedFuture,

    /// <summary>The effective file does not exist (removed while the app was running).</summary>
    Missing,

    /// <summary>The effective file could not be read (locked, access denied, I/O failure).</summary>
    ReadError,
}

/// <summary>
/// Result of reading one external <c>panel.json</c> candidate without touching the file system
/// beyond the read (M2-RF-822): the candidate configuration when valid, its raw text (used for the
/// content-unchanged no-op and for recording the applied content), and diagnostics for logging.
/// </summary>
public sealed record PanelConfigCandidate
{
    /// <summary>Gets how the candidate was classified.</summary>
    public required PanelConfigCandidateOutcome Outcome { get; init; }

    /// <summary>Gets the migrated and validated configuration when <see cref="Outcome"/> is <see cref="PanelConfigCandidateOutcome.Valid"/>; otherwise <see langword="null"/>.</summary>
    public PanelConfig? Config { get; init; }

    /// <summary>Gets the schema version detected in the file, when one could be probed.</summary>
    public int? DetectedSchemaVersion { get; init; }

    /// <summary>Gets the exact file text read, or <see langword="null"/> when the file could not be read.</summary>
    public string? RawContent { get; init; }

    /// <summary>Gets the diagnostics gathered while probing, migrating, and validating; never <see langword="null"/>.</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Gets a value indicating whether the candidate may be applied.</summary>
    public bool IsValid => Outcome == PanelConfigCandidateOutcome.Valid && Config is not null;

    /// <summary>Creates a candidate that may be applied.</summary>
    /// <param name="config">The migrated, validated configuration.</param>
    /// <param name="rawContent">The exact file text.</param>
    /// <param name="detectedSchemaVersion">The schema version found in the file.</param>
    /// <param name="diagnostics">Migration/validation diagnostics.</param>
    /// <returns>The valid candidate.</returns>
    public static PanelConfigCandidate Valid(
        PanelConfig config,
        string rawContent,
        int detectedSchemaVersion,
        IReadOnlyList<ConfigDiagnostic> diagnostics) => new()
    {
        Outcome = PanelConfigCandidateOutcome.Valid,
        Config = config,
        RawContent = rawContent,
        DetectedSchemaVersion = detectedSchemaVersion,
        Diagnostics = diagnostics,
    };

    /// <summary>Creates a rejected candidate.</summary>
    /// <param name="outcome">The non-valid classification.</param>
    /// <param name="detectedSchemaVersion">The detected schema version, when known.</param>
    /// <param name="rawContent">The exact file text when it was readable; otherwise <see langword="null"/>.</param>
    /// <param name="diagnostics">The diagnostics explaining the rejection.</param>
    /// <returns>The rejected candidate.</returns>
    public static PanelConfigCandidate Rejected(
        PanelConfigCandidateOutcome outcome,
        int? detectedSchemaVersion,
        string? rawContent,
        IReadOnlyList<ConfigDiagnostic> diagnostics) => new()
    {
        Outcome = outcome,
        DetectedSchemaVersion = detectedSchemaVersion,
        RawContent = rawContent,
        Diagnostics = diagnostics,
    };
}

/// <summary>
/// Reader boundary for the hot-reload apply pipeline (M2-RF-822/M2-RF-830). A production
/// implementation reads the effective <c>panel.json</c>, probes its schema version, migrates older
/// supported files in memory, and validates the whole candidate; it never writes the watched file
/// and never recovers/preserves a corrupt source (that remains a startup-load concern, RF-075).
/// </summary>
public interface IPanelConfigCandidateReader
{
    /// <summary>
    /// Reads and classifies the effective configuration file as a whole.
    /// </summary>
    /// <param name="cancellationToken">Cancels the file read.</param>
    /// <returns>The classified candidate; never <see langword="null"/>.</returns>
    ValueTask<PanelConfigCandidate> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Production <see cref="IPanelConfigCandidateReader"/> (M2-RF-822/M2-RF-825/M2-RF-826): reads the
/// effective file, performs the same file-level probe as <c>JsonConfigStore</c> (JSON syntax, object
/// root, integer <c>schemaVersion</c>), refuses to interpret a newer schema, migrates older
/// supported files in memory through <see cref="MigrationRunner"/>, and validates the result with
/// <see cref="ConfigValidator"/>. It is WPF-free and performs no write, backup, or preservation, so
/// the hot-reload path can never alter the user's file.
/// </summary>
public sealed class PanelConfigCandidateReader : IPanelConfigCandidateReader
{
    private readonly ConfigPaths _paths;
    private readonly MigrationRunner _migrations;
    private readonly ILogger _logger;
    private readonly IClockCultureSource? _clockCulture;

    /// <summary>
    /// Initializes a new instance of the <see cref="PanelConfigCandidateReader"/> class.
    /// </summary>
    /// <param name="paths">Injected file paths; defaults to the real per-user profile (tests inject a temp directory).</param>
    /// <param name="migrations">The migration chain; defaults to the production v1 baseline.</param>
    /// <param name="logger">Logger for diagnostics; defaults to the global Serilog logger.</param>
    /// <param name="clockCulture">
    /// The canonical Clock culture source (M4-RF-951) threaded to candidate validation so a
    /// hot-reloaded or imported custom Clock format is checked against the same display culture as
    /// runtime; <see langword="null"/> uses <see cref="ClockCultureSource.Default"/>.
    /// </param>
    public PanelConfigCandidateReader(
        ConfigPaths? paths = null,
        MigrationRunner? migrations = null,
        ILogger? logger = null,
        IClockCultureSource? clockCulture = null)
    {
        _paths = paths ?? ConfigPaths.Default;
        _clockCulture = clockCulture;
        _migrations = migrations ?? new MigrationRunner(ConfigMigrationChain.Create(), clockCulture: clockCulture);
        _logger = (logger ?? Log.Logger).ForContext<PanelConfigCandidateReader>();
    }

    /// <inheritdoc />
    public ValueTask<PanelConfigCandidate> ReadAsync(CancellationToken cancellationToken = default) =>
        ReadFileAsync(_paths.PanelFile, cancellationToken);

    /// <summary>
    /// Reads and classifies an arbitrary configuration file as a whole, using the exact same
    /// file-level probe (JSON syntax, object root, integer <c>schemaVersion</c>), future-schema
    /// refusal, in-memory migration of older supported schemas, and whole-file validation as
    /// <see cref="ReadAsync"/>. Import (M2-RF-832) reuses this method so import and hot reload
    /// classify a file identically; like the reload path it never writes or preserves the file.
    /// </summary>
    /// <param name="path">The full path of the configuration file to read.</param>
    /// <param name="cancellationToken">Cancels the file read.</param>
    /// <returns>The classified candidate; never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
    public async ValueTask<PanelConfigCandidate> ReadFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return PanelConfigCandidate.Rejected(
                PanelConfigCandidateOutcome.Missing,
                detectedSchemaVersion: null,
                rawContent: null,
                [ConfigDiagnostic.Warning("config.reload.missing", "The configuration file no longer exists; the running configuration is kept.")]);
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Hot reload: the configuration file could not be read");
            return PanelConfigCandidate.Rejected(
                PanelConfigCandidateOutcome.ReadError,
                detectedSchemaVersion: null,
                rawContent: null,
                [ConfigDiagnostic.Error("config.reload.readFailed", "The configuration file could not be read; the running configuration is kept.")]);
        }

        if (!TryProbe(text, out var version, out var problemCode, out var problemMessage))
        {
            return PanelConfigCandidate.Rejected(
                PanelConfigCandidateOutcome.Invalid,
                detectedSchemaVersion: null,
                rawContent: text,
                [ConfigDiagnostic.Error(problemCode, problemMessage)]);
        }

        if (version > PanelConfig.CurrentSchemaVersion)
        {
            return PanelConfigCandidate.Rejected(
                PanelConfigCandidateOutcome.UnsupportedFuture,
                version,
                text,
                [ConfigDiagnostic.Error(
                    "config.reload.futureSchema",
                    $"Schema version {version} is newer than the supported version {PanelConfig.CurrentSchemaVersion}; the file was not interpreted.")]);
        }

        PanelConfig? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PanelConfig>(text, ConfigJson.Options);
        }
        catch (JsonException ex)
        {
            return PanelConfigCandidate.Rejected(
                PanelConfigCandidateOutcome.Invalid,
                version,
                text,
                [ConfigDiagnostic.Error(
                    "config.reload.structure",
                    LogRedactor.RedactText($"The configuration is structurally invalid ({ex.GetType().Name}: {ex.Message})."))]);
        }

        if (parsed is null)
        {
            return PanelConfigCandidate.Rejected(
                PanelConfigCandidateOutcome.Invalid,
                version,
                text,
                [ConfigDiagnostic.Error("config.reload.structure", "The configuration deserialized to no value.")]);
        }

        if (version == PanelConfig.CurrentSchemaVersion)
        {
            var diagnostics = ConfigValidator.Validate(parsed, _clockCulture);
            return PanelConfigCandidate.Valid(parsed, text, version, diagnostics);
        }

        MigrationResult migration;
        try
        {
            migration = _migrations.Migrate(parsed, version);
        }
        catch (Exception ex)
        {
            return PanelConfigCandidate.Rejected(
                PanelConfigCandidateOutcome.Invalid,
                version,
                text,
                [ConfigDiagnostic.Error(
                    "config.reload.migrationFailed",
                    LogRedactor.RedactText(
                        $"Migration from schema {version} failed ({ex.GetType().Name}: {ex.Message}); the running configuration is kept."))]);
        }

        return PanelConfigCandidate.Valid(migration.Config, text, version, migration.Diagnostics);
    }

    /// <summary>
    /// File-level structural probe (RF-079): JSON syntax, object root, and an integer
    /// <c>schemaVersion</c>. Mirrors the store's probe so both paths classify a file identically.
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
            problemCode = "config.reload.parseFailed";
            problemMessage = LogRedactor.RedactText(
                $"The configuration is not valid JSON ({ex.GetType().Name}: {ex.Message}).");
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problemCode = "config.reload.rootInvalid";
                problemMessage = "The configuration root must be a JSON object.";
                return false;
            }

            if (!root.TryGetProperty("schemaVersion", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out version))
            {
                problemCode = "config.reload.schemaMissing";
                problemMessage = "The configuration must contain an integer 'schemaVersion'.";
                return false;
            }
        }

        return true;
    }
}
