using Serilog;

namespace TouchPanel.Services.Logging;

/// <summary>
/// File-sink settings for the application log. The defaults implement RF-009/D-16
/// (5 files × 2 MB) and are asserted by tests; they can be overridden for tests that
/// exercise rotation with tiny files.
/// </summary>
public sealed record LoggingOptions
{
    /// <summary>Default log file name template; Serilog produces e.g. <c>touchpanel-20261008.log</c>.</summary>
    public const string DefaultFileName = "touchpanel-.log";

    /// <summary>Default number of retained log files (D-16: 5 files).</summary>
    public const int DefaultRetainedFileCountLimit = 5;

    /// <summary>Default size of a single log file in bytes (D-16: 2 MB).</summary>
    public const long DefaultFileSizeLimitBytes = 2L * 1024 * 1024;

    /// <summary>Gets the directory that receives the rolling log files (typically <see cref="AppPaths.LogsDirectory"/>).</summary>
    public required string LogDirectory { get; init; }

    /// <summary>Gets the rolling file name; the period and a sequence number are inserted before the extension.</summary>
    public string FileName { get; init; } = DefaultFileName;

    /// <summary>Gets the maximum number of log files kept on disk (RF-009/D-16).</summary>
    public int RetainedFileCountLimit { get; init; } = DefaultRetainedFileCountLimit;

    /// <summary>Gets the maximum size of a single log file in bytes before it rolls (RF-009/D-16).</summary>
    public long FileSizeLimitBytes { get; init; } = DefaultFileSizeLimitBytes;

    /// <summary>Gets the rolling cadence; daily files bound retention to the configured file count.</summary>
    public RollingInterval RollingInterval { get; init; } = RollingInterval.Day;
}
