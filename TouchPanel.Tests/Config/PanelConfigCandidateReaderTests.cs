using System.IO;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Temp-directory checks for the hot-reload candidate reader (T-11 part 2,
/// M2-RF-822/M2-RF-825/M2-RF-826): a reload reads the whole file, migrates older supported schemas
/// in memory, validates the result, refuses a future schema, and never writes the watched file.
/// </summary>
public sealed class PanelConfigCandidateReaderTests
{
    [Fact]
    public async Task ValidCurrentSchema_IsReadAndValidated()
    {
        using var temp = new TempDirectory();
        var text = """{"schemaVersion":4,"activePageId":"main","pages":[{"id":"main","widgets":[]}]}""";
        await File.WriteAllTextAsync(temp.Config.PanelFile, text);

        var candidate = await CreateReader(temp).ReadAsync();

        Assert.Equal(PanelConfigCandidateOutcome.Valid, candidate.Outcome);
        Assert.True(candidate.IsValid);
        Assert.NotNull(candidate.Config);
        Assert.Equal(PanelConfig.CurrentSchemaVersion, candidate.Config!.SchemaVersion);
        Assert.Equal(text, candidate.RawContent);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{"activePageId":"main"}""")]
    public async Task MalformedOrUnversioned_IsRejectedAsInvalid(string text)
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(temp.Config.PanelFile, text);

        var candidate = await CreateReader(temp).ReadAsync();

        Assert.Equal(PanelConfigCandidateOutcome.Invalid, candidate.Outcome);
        Assert.False(candidate.IsValid);
        Assert.Contains(candidate.Diagnostics, d => d.Severity == ConfigDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task NewerSchema_IsNotInterpreted()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(temp.Config.PanelFile, """{"schemaVersion":99}""");

        var candidate = await CreateReader(temp).ReadAsync();

        Assert.Equal(PanelConfigCandidateOutcome.UnsupportedFuture, candidate.Outcome);
        Assert.Null(candidate.Config);
        Assert.Equal(99, candidate.DetectedSchemaVersion);
        Assert.Equal(
            """{"schemaVersion":99}""",
            await File.ReadAllTextAsync(temp.Config.PanelFile));
    }

    [Fact]
    public async Task OlderSupportedSchema_IsMigratedInMemory()
    {
        using var temp = new TempDirectory();
        var text = """{"schemaVersion":1,"activePageId":"main","pages":[{"id":"main","widgets":[]}]}""";
        await File.WriteAllTextAsync(temp.Config.PanelFile, text);

        var candidate = await CreateReader(temp).ReadAsync();

        Assert.Equal(PanelConfigCandidateOutcome.Valid, candidate.Outcome);
        Assert.NotNull(candidate.Config);
        Assert.Equal(PanelConfig.CurrentSchemaVersion, candidate.Config!.SchemaVersion);
        Assert.Equal(1, candidate.DetectedSchemaVersion);

        // The reader never writes the watched file: the older source is left exactly as written.
        Assert.Equal(text, await File.ReadAllTextAsync(temp.Config.PanelFile));
    }

    [Fact]
    public async Task MissingFile_IsReportedAsMissing()
    {
        using var temp = new TempDirectory();

        var candidate = await CreateReader(temp).ReadAsync();

        Assert.Equal(PanelConfigCandidateOutcome.Missing, candidate.Outcome);
        Assert.False(File.Exists(temp.Config.BackupFile));
    }

    private static PanelConfigCandidateReader CreateReader(TempDirectory temp) =>
        new(temp.Config, logger: null);
}
