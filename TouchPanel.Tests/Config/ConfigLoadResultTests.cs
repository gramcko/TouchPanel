using TouchPanel.Config;

namespace TouchPanel.Tests.Config;

public class ConfigLoadResultTests
{
    [Fact]
    public void ConfigState_ExposesExactlyTheFourSpecifiedStates()
    {
        var states = Enum.GetValues<ConfigState>();

        Assert.Equal(4, states.Length);
        Assert.Equal(
            [ConfigState.Current, ConfigState.OlderSupported, ConfigState.Invalid, ConfigState.UnsupportedFuture],
            states);
    }

    [Fact]
    public void Current_CarriesConfigAndDefaultsToNoDiagnostics()
    {
        var config = new PanelConfig { SchemaVersion = PanelConfig.CurrentSchemaVersion };

        var result = new ConfigLoadResult
        {
            State = ConfigState.Current,
            Config = config,
            DetectedSchemaVersion = PanelConfig.CurrentSchemaVersion,
        };

        Assert.Equal(ConfigState.Current, result.State);
        Assert.Same(config, result.Config);
        Assert.Equal(PanelConfig.CurrentSchemaVersion, result.DetectedSchemaVersion);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void OlderSupported_CarriesDetectedVersionAndMigratedConfig()
    {
        var config = new PanelConfig { SchemaVersion = PanelConfig.CurrentSchemaVersion };

        var result = new ConfigLoadResult
        {
            State = ConfigState.OlderSupported,
            Config = config,
            DetectedSchemaVersion = 0,
            Diagnostics = [ConfigDiagnostic.Info("config.migrated", "Migrated schema 0 to 1.")],
        };

        Assert.Equal(0, result.DetectedSchemaVersion);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Invalid_CarriesErrorDiagnosticsAndMayHaveNoConfig()
    {
        var result = new ConfigLoadResult
        {
            State = ConfigState.Invalid,
            Diagnostics =
            [
                ConfigDiagnostic.Error("config.parse", "The file is not valid JSON."),
                ConfigDiagnostic.Warning("config.entrySkipped", "Skipped an unknown widget type."),
            ],
        };

        Assert.Null(result.Config);
        Assert.Null(result.DetectedSchemaVersion);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == ConfigDiagnosticSeverity.Error);
    }

    [Fact]
    public void UnsupportedFuture_CarriesDetectedVersionWithoutConfig()
    {
        var result = new ConfigLoadResult
        {
            State = ConfigState.UnsupportedFuture,
            DetectedSchemaVersion = 99,
            Diagnostics = [ConfigDiagnostic.Error("config.futureVersion", "Schema 99 is not supported.")],
        };

        Assert.Equal(99, result.DetectedSchemaVersion);
        Assert.Null(result.Config);
    }

    [Theory]
    [InlineData(ConfigState.Current)]
    [InlineData(ConfigState.OlderSupported)]
    [InlineData(ConfigState.Invalid)]
    [InlineData(ConfigState.UnsupportedFuture)]
    public void EveryState_CanBeConstructed(ConfigState state)
    {
        var result = new ConfigLoadResult { State = state };

        Assert.Equal(state, result.State);
        Assert.NotNull(result.Diagnostics);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void DiagnosticFactories_SetSeverityAndPreserveCodeAndMessage()
    {
        var info = ConfigDiagnostic.Info("config.missing", "Generated defaults.");
        var warning = ConfigDiagnostic.Warning("config.widget.skipped", "Unknown widget type.");
        var error = ConfigDiagnostic.Error("config.parse", "Invalid JSON.");

        Assert.Equal(ConfigDiagnosticSeverity.Info, info.Severity);
        Assert.Equal(ConfigDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(ConfigDiagnosticSeverity.Error, error.Severity);
        Assert.Equal("config.missing", info.Code);
        Assert.Equal("Unknown widget type.", warning.Message);
        Assert.Equal("config.parse", error.Code);
    }
}
