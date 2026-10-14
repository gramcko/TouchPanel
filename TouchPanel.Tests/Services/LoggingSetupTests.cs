using System.IO;
using Serilog;
using TouchPanel.Services.Logging;

namespace TouchPanel.Tests.Services;

public sealed class LoggingSetupTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "TouchPanel.Tests", Guid.NewGuid().ToString("N"));

    public LoggingSetupTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup only; a locked file must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }

    [Fact]
    public void Defaults_MatchRequiredRotationSettings()
    {
        var options = new LoggingOptions { LogDirectory = _tempDirectory };

        Assert.Equal(5, options.RetainedFileCountLimit);
        Assert.Equal(2L * 1024 * 1024, options.FileSizeLimitBytes);
        Assert.Equal(RollingInterval.Day, options.RollingInterval);
        Assert.Equal("touchpanel-.log", options.FileName);
    }

    [Fact]
    public void CreateLogger_WritesStartupAndShutdownEntries_ToTheLogDirectory()
    {
        var initialization = LoggingSetup.CreateLogger(new LoggingOptions { LogDirectory = _tempDirectory });

        Assert.True(initialization.FileLoggingEnabled);
        Assert.Null(initialization.FailureReason);

        AppLog.Startup(initialization.Logger, _tempDirectory);
        AppLog.Shutdown(initialization.Logger);
        initialization.Logger.Dispose();

        var logFile = Assert.Single(Directory.GetFiles(_tempDirectory, "touchpanel-*.log"));
        Assert.StartsWith("touchpanel-", Path.GetFileName(logFile), StringComparison.OrdinalIgnoreCase);

        var content = File.ReadAllText(logFile);
        Assert.Contains(AppLog.StartupMessage, content, StringComparison.Ordinal);
        Assert.Contains(AppLog.ShutdownMessage, content, StringComparison.Ordinal);
        Assert.Contains(_tempDirectory, content, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateLogger_UnwritableLogPath_DegradesWithoutThrowing()
    {
        // A file used as a directory component is deterministically unwritable.
        var occupiedPath = Path.Combine(_tempDirectory, "occupied.txt");
        File.WriteAllText(occupiedPath, "not a directory");

        var initialization = LoggingSetup.CreateLogger(new LoggingOptions
        {
            LogDirectory = Path.Combine(occupiedPath, "logs"),
        });

        Assert.False(initialization.FileLoggingEnabled);
        Assert.NotNull(initialization.FailureReason);

        // The fallback logger must stay usable and must never throw (RF-010).
        var exception = Record.Exception(() =>
        {
            AppLog.Startup(initialization.Logger, _tempDirectory);
            initialization.Logger.Warning("fallback still logs");
        });
        Assert.Null(exception);
        initialization.Logger.Dispose();
    }

    [Fact]
    public void CreateLogger_Rotation_KeepsAtMostTheConfiguredNumberOfFiles()
    {
        var initialization = LoggingSetup.CreateLogger(new LoggingOptions
        {
            LogDirectory = _tempDirectory,
            FileSizeLimitBytes = 2048,
            RetainedFileCountLimit = 3,
        });

        Assert.True(initialization.FileLoggingEnabled);
        try
        {
            for (var i = 0; i < 300; i++)
            {
                initialization.Logger.Information(
                    "Rotation payload {Index} {Payload}", i, new string('x', 120));
            }
        }
        finally
        {
            initialization.Logger.Dispose();
        }

        var files = Directory.GetFiles(_tempDirectory, "touchpanel-*.log");
        Assert.True(files.Length >= 2, $"expected rotation to produce multiple files, found {files.Length}.");
        Assert.True(files.Length <= 3, $"expected at most 3 retained files, found {files.Length}.");
    }
}
