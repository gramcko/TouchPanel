using System.IO;
using System.Text.Json;
using Serilog;
using Serilog.Events;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Services.Logging;
using TouchPanel.Services.Startup;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Services;

/// <summary>
/// T-06/RF-003/RF-004: per-user start-with-Windows registration and self-heal. Every test injects
/// an in-memory <see cref="IStartupRegistry"/> so the real HKCU Run key is never touched
/// (RNF-006); the production <c>HkcuRunRegistry</c> is intentionally not exercised here.
/// </summary>
public sealed class RegistryStartupRegistrationTests
{
    private const string ExePath = @"C:\Apps\TouchPanel\TouchPanel.exe";
    private const string QuotedExePath = "\"" + ExePath + "\"";

    // --- DD-4 contract ------------------------------------------------------------------------

    [Fact]
    public void ProductionEntry_LivesUnderHkcuRunWithTheTouchPanelName()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", RegistryStartupRegistration.RegistryPath);
        Assert.Equal("TouchPanel", RegistryStartupRegistration.ValueName);
    }

    // --- read ---------------------------------------------------------------------------------

    [Fact]
    public void Read_WhenEntryIsMissing_ReportsMissingAndNotCurrent()
    {
        var registry = new FakeStartupRegistry { Command = null };
        var (registration, _) = Create(registry);

        var snapshot = registration.Read();

        Assert.True(snapshot.Succeeded);
        Assert.False(snapshot.Exists);
        Assert.False(snapshot.IsCurrent);
        Assert.Null(snapshot.Command);
        Assert.Equal(1, registry.ReadCount);
    }

    [Fact]
    public void Read_WhenEntryMatchesTheQuotedCurrentPath_ReportsCurrent()
    {
        var registry = new FakeStartupRegistry { Command = QuotedExePath };
        var (registration, _) = Create(registry);

        var snapshot = registration.Read();

        Assert.True(snapshot.Succeeded);
        Assert.True(snapshot.Exists);
        Assert.True(snapshot.IsCurrent);
        Assert.Equal(QuotedExePath, snapshot.Command);
    }

    [Fact]
    public void Read_WhenEntryMatchesWithDifferentCasing_ReportsCurrent()
    {
        // Windows paths are case-insensitive; equal paths in different casing are not stale.
        var registry = new FakeStartupRegistry { Command = "\"c:\\apps\\touchpanel\\touchpanel.exe\"" };
        var (registration, _) = Create(registry);

        Assert.True(registration.Read().IsCurrent);
    }

    [Theory]
    [InlineData(@"C:\Other\TouchPanel.exe")]                              // different unquoted path
    [InlineData("\"C:\\Other\\TouchPanel.exe\"")]                         // different quoted path
    [InlineData(ExePath)]                                                  // same path but unquoted
    [InlineData("\"" + ExePath + "\" --minimized")]                        // same path with extra arguments
    [InlineData("   ")]                                                    // blank
    [InlineData("")]                                                       // empty
    public void Read_WhenEntryIsStale_ReportsNotCurrent(string command)
    {
        var registry = new FakeStartupRegistry { Command = command };
        var (registration, _) = Create(registry);

        var snapshot = registration.Read();

        Assert.True(snapshot.Succeeded);
        Assert.True(snapshot.Exists);
        Assert.False(snapshot.IsCurrent);
    }

    [Fact]
    public void Read_WhenRegistryThrows_ReturnsFailureAndLogsWithoutThrowing()
    {
        var registry = new FakeStartupRegistry
        {
            ReadException = new UnauthorizedAccessException("access denied"),
        };
        var (registration, sink) = Create(registry);

        StartupRegistrationSnapshot? snapshot = null;
        var exception = Record.Exception(() => snapshot = registration.Read());

        Assert.Null(exception);
        Assert.NotNull(snapshot);
        Assert.False(snapshot.Succeeded);
        Assert.False(snapshot.Exists);
        Assert.False(snapshot.IsCurrent);
        Assert.Contains("UnauthorizedAccessException", snapshot.FailureMessage, StringComparison.Ordinal);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Error);
    }

    // --- apply: enabled (create + self-heal) --------------------------------------------------

    [Fact]
    public void Apply_EnabledAndMissing_RegistersTheQuotedCurrentPathAndWarns()
    {
        var registry = new FakeStartupRegistry();
        var (registration, sink) = Create(registry);

        var result = registration.Apply(startWithWindows: true);

        Assert.True(result.Succeeded);
        Assert.Equal(StartupRegistrationAction.Registered, result.Action);
        Assert.Null(result.FailureCode);
        Assert.Equal(QuotedExePath, registry.Command);
        Assert.Equal(1, registry.WriteCount);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
    }

    [Fact]
    public void Apply_EnabledAndDifferentPath_ReRegistersWithAWarning()
    {
        var registry = new FakeStartupRegistry { Command = "\"C:\\Old\\TouchPanel.exe\"" };
        var (registration, sink) = Create(registry);

        var result = registration.Apply(true);

        Assert.True(result.Succeeded);
        Assert.Equal(StartupRegistrationAction.Registered, result.Action);
        Assert.Equal(QuotedExePath, registry.Command);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
    }

    [Fact]
    public void Apply_EnabledAndUnquotedPath_ReRegisters()
    {
        var registry = new FakeStartupRegistry { Command = ExePath };
        var (registration, _) = Create(registry);

        var result = registration.Apply(true);

        Assert.True(result.Succeeded);
        Assert.Equal(StartupRegistrationAction.Registered, result.Action);
        Assert.Equal(QuotedExePath, registry.Command);
    }

    [Fact]
    public void Apply_EnabledAndBlankValue_ReRegisters()
    {
        var registry = new FakeStartupRegistry { Command = "   " };
        var (registration, _) = Create(registry);

        var result = registration.Apply(true);

        Assert.True(result.Succeeded);
        Assert.Equal(StartupRegistrationAction.Registered, result.Action);
        Assert.Equal(QuotedExePath, registry.Command);
    }

    [Fact]
    public void Apply_EnabledWithSpacesInPath_RegistersTheQuotedPath()
    {
        const string pathWithSpaces = @"C:\Program Files\TouchPanel\TouchPanel.exe";
        var registry = new FakeStartupRegistry();
        var (registration, _) = Create(registry, pathWithSpaces);

        var result = registration.Apply(true);

        Assert.True(result.Succeeded);
        Assert.Equal("\"" + pathWithSpaces + "\"", registry.Command);
    }

    [Fact]
    public void Apply_EnabledAndCurrent_ChangesNothing()
    {
        var registry = new FakeStartupRegistry { Command = QuotedExePath };
        var (registration, sink) = Create(registry);

        var result = registration.Apply(true);

        Assert.True(result.Succeeded);
        Assert.Equal(StartupRegistrationAction.None, result.Action);
        Assert.Equal(0, registry.WriteCount);
        Assert.Equal(0, registry.RemoveCount);
        Assert.DoesNotContain(sink.Events, logEvent => logEvent.Level >= LogEventLevel.Warning);
    }

    // --- apply: disabled (remove) -------------------------------------------------------------

    [Fact]
    public void Apply_DisabledAndPresent_RemovesTheEntry()
    {
        var registry = new FakeStartupRegistry { Command = QuotedExePath };
        var (registration, _) = Create(registry);

        var result = registration.Apply(false);

        Assert.True(result.Succeeded);
        Assert.Equal(StartupRegistrationAction.Removed, result.Action);
        Assert.Null(registry.Command);
        Assert.Equal(1, registry.RemoveCount);
        Assert.Equal(0, registry.WriteCount);
    }

    [Fact]
    public void Apply_DisabledAndMissing_ChangesNothing()
    {
        var registry = new FakeStartupRegistry();
        var (registration, _) = Create(registry);

        var result = registration.Apply(false);

        Assert.True(result.Succeeded);
        Assert.Equal(StartupRegistrationAction.None, result.Action);
        Assert.Equal(0, registry.WriteCount);
        Assert.Equal(0, registry.RemoveCount);
        Assert.Equal(1, registry.ReadCount);
    }

    // --- apply: unusable executable path ------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_EnabledWithoutExecutablePath_FailsWithoutTouchingTheRegistry(string? executablePath)
    {
        var registry = new FakeStartupRegistry();
        var (registration, sink) = Create(registry, executablePath);

        var result = registration.Apply(true);

        Assert.False(result.Succeeded);
        Assert.Equal("startup.path.unavailable", result.FailureCode);
        Assert.NotNull(result.Message);
        Assert.Equal(0, registry.ReadCount);
        Assert.Equal(0, registry.WriteCount);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Error);
    }

    // --- failure paths: logged, reported, never thrown ----------------------------------------

    [Fact]
    public void Apply_WhenReadThrows_ReturnsFailureAndNeverThrows()
    {
        var registry = new FakeStartupRegistry
        {
            ReadException = new UnauthorizedAccessException("denied"),
        };
        var (registration, sink) = Create(registry);

        StartupRegistrationResult? result = null;
        var exception = Record.Exception(() => result = registration.Apply(true));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal("startup.read.failed", result.FailureCode);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Error);
    }

    [Fact]
    public void Apply_WhenWriteThrows_ReturnsFailureAndNeverThrows()
    {
        var registry = new FakeStartupRegistry
        {
            WriteException = new UnauthorizedAccessException("denied"),
        };
        var (registration, sink) = Create(registry);

        StartupRegistrationResult? result = null;
        var exception = Record.Exception(() => result = registration.Apply(true));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal("startup.write.failed", result.FailureCode);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Error);
    }

    [Fact]
    public void Apply_WhenRemoveThrows_ReturnsFailureAndNeverThrows()
    {
        var registry = new FakeStartupRegistry
        {
            Command = QuotedExePath,
            RemoveException = new UnauthorizedAccessException("denied"),
        };
        var (registration, sink) = Create(registry);

        StartupRegistrationResult? result = null;
        var exception = Record.Exception(() => result = registration.Apply(false));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal("startup.remove.failed", result.FailureCode);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Error);
    }

    [Fact]
    public void Apply_WhenFailureMentionsASecret_MessageIsRedacted()
    {
        var registry = new FakeStartupRegistry
        {
            WriteException = new InvalidOperationException("failed with token=SuperSecret123"),
        };
        var (registration, _) = Create(registry);

        var result = registration.Apply(true);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Message);
        Assert.DoesNotContain("SuperSecret123", result.Message, StringComparison.Ordinal);
        Assert.Contains(LogRedactor.Mask, result.Message, StringComparison.Ordinal);
    }

    // --- integration with the loaded configuration (temp-dir paths only) ------------------------

    [Fact]
    public async Task Apply_WhenLoadedConfigEnablesStartWithWindows_RegistersTheEntry()
    {
        using var temp = new TempDirectory();
        var config = DefaultConfigGenerator.Create();
        config.Startup.StartWithWindows = true;
        WritePanelConfig(temp, config);
        var (store, _) = CreateStore(temp);
        var load = await store.LoadAsync();
        var registry = new FakeStartupRegistry();
        var (registration, _) = Create(registry);

        var result = registration.Apply(load.Config!.Startup.StartWithWindows);

        Assert.Equal(ConfigState.Current, load.State);
        Assert.True(result.Succeeded);
        Assert.Equal(StartupRegistrationAction.Registered, result.Action);
        Assert.Equal(QuotedExePath, registry.Command);
    }

    [Fact]
    public async Task Apply_WhenLoadedConfigDisablesStartWithWindows_RemovesAnExistingEntry()
    {
        using var temp = new TempDirectory();
        var config = DefaultConfigGenerator.Create();
        config.Startup.StartWithWindows = false;
        WritePanelConfig(temp, config);
        var (store, _) = CreateStore(temp);
        var load = await store.LoadAsync();
        var registry = new FakeStartupRegistry { Command = QuotedExePath };
        var (registration, _) = Create(registry);

        var result = registration.Apply(load.Config!.Startup.StartWithWindows);

        Assert.Equal(ConfigState.Current, load.State);
        Assert.True(result.Succeeded);
        Assert.Equal(StartupRegistrationAction.Removed, result.Action);
        Assert.Null(registry.Command);
    }

    [Fact]
    public async Task Apply_WhenLoadedConfigEnablesStartWithWindowsButTheEntryIsStale_SelfHealsIt()
    {
        using var temp = new TempDirectory();
        var config = DefaultConfigGenerator.Create();
        config.Startup.StartWithWindows = true;
        WritePanelConfig(temp, config);
        var (store, _) = CreateStore(temp);
        var load = await store.LoadAsync();
        var registry = new FakeStartupRegistry { Command = "\"C:\\Old\\TouchPanel.exe\"" };
        var (registration, sink) = Create(registry);

        var result = registration.Apply(load.Config!.Startup.StartWithWindows);

        Assert.True(result.Succeeded);
        Assert.Equal(StartupRegistrationAction.Registered, result.Action);
        Assert.Equal(QuotedExePath, registry.Command);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
    }

    // --- test infrastructure ------------------------------------------------------------------

    private static (RegistryStartupRegistration Registration, RecordingSink Sink) Create(
        FakeStartupRegistry registry,
        string? executablePath = ExePath)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new RegistryStartupRegistration(registry, executablePath, logger), sink);
    }

    private static (JsonConfigStore Store, RecordingSink Sink) CreateStore(TempDirectory temp)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        return (new JsonConfigStore(temp.Config, logger: logger), sink);
    }

    private static void WritePanelConfig(TempDirectory temp, PanelConfig config) =>
        File.WriteAllText(temp.Config.PanelFile, JsonSerializer.Serialize(config, ConfigJson.Options));

    /// <summary>In-memory <see cref="IStartupRegistry"/>; the real HKCU Run key is never touched.</summary>
    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        public string? Command { get; set; }

        public Exception? ReadException { get; set; }

        public Exception? WriteException { get; set; }

        public Exception? RemoveException { get; set; }

        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public int RemoveCount { get; private set; }

        public string? ReadCommand()
        {
            ReadCount++;
            if (ReadException is not null)
            {
                throw ReadException;
            }

            return Command;
        }

        public void WriteCommand(string command)
        {
            WriteCount++;
            if (WriteException is not null)
            {
                throw WriteException;
            }

            Command = command;
        }

        public void RemoveCommand()
        {
            RemoveCount++;
            if (RemoveException is not null)
            {
                throw RemoveException;
            }

            Command = null;
        }
    }
}
