using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Validation;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Global appearance configuration (T-08, M3-RF-841/M3-RF-847): the schema-v4 <c>appearance</c>
/// section round-trips, a missing section uses the documented defaults, and validation contains
/// unknown theme/icon-pack ids and invalid/blank accent colors entry-level with a warning (never a
/// file-level error).
/// </summary>
public sealed class AppearanceConfigTests
{
    private const string BaseJson = """
        {
          "schemaVersion": 4,
          "activePageId": "main",
          "pages": [
            {
              "id": "main",
              "widgets": [
                { "type": "label", "id": "l1", "x": 0, "y": 0, "width": 100, "height": 40, "label": "Hi" }
              ]
            }
          ]{0}
        }
        """;

    [Fact]
    public void Appearance_RoundTrips()
    {
        var original = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Appearance = new AppearanceConfig
            {
                Theme = "light",
                AccentColor = "#AABBCC",
                IconPack = "filled",
            },
            Pages = [new PageConfig { Id = "main" }],
        };

        var json = JsonSerializer.Serialize(original, ConfigJson.Options);
        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.Equal("light", restored.Appearance.Theme);
        Assert.Equal("#AABBCC", restored.Appearance.AccentColor);
        Assert.Equal("filled", restored.Appearance.IconPack);
        Assert.Contains("\"appearance\"", json, StringComparison.Ordinal);
        Assert.Contains("\"iconPack\": \"filled\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAppearanceSection_UsesDefaultsWithoutWarnings()
    {
        var config = Parse(BaseJson.Replace("{0}", string.Empty, StringComparison.Ordinal));

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal("dark", config.Appearance.Theme);
        Assert.Null(config.Appearance.AccentColor);
        Assert.Equal("default", config.Appearance.IconPack);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code.StartsWith("config.appearance", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyAppearanceSection_UsesDefaults()
    {
        var config = Parse(BaseJson.Replace("{0}", ",\n  \"appearance\": {}", StringComparison.Ordinal));

        ConfigValidator.Validate(config);

        Assert.Equal("dark", config.Appearance.Theme);
        Assert.Null(config.Appearance.AccentColor);
        Assert.Equal("default", config.Appearance.IconPack);
    }

    [Fact]
    public void ExplicitNullAppearanceSection_UsesDefaults()
    {
        var config = Parse(BaseJson.Replace("{0}", ",\n  \"appearance\": null", StringComparison.Ordinal));

        ConfigValidator.Validate(config);

        Assert.Equal("dark", config.Appearance.Theme);
        Assert.Equal("default", config.Appearance.IconPack);
    }

    [Fact]
    public void UnknownTheme_FallsBackToDarkWithAWarning()
    {
        var config = Parse(BaseJson.Replace("{0}", ",\n  \"appearance\": { \"theme\": \"neon\" }", StringComparison.Ordinal));

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal("dark", config.Appearance.Theme);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.appearance.theme");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == ConfigDiagnosticSeverity.Error);
    }

    [Fact]
    public void UnknownIconPack_FallsBackToDefaultWithAWarning()
    {
        var config = Parse(BaseJson.Replace("{0}", ",\n  \"appearance\": { \"iconPack\": \"hand-drawn\" }", StringComparison.Ordinal));

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal("default", config.Appearance.IconPack);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.appearance.iconPack");
    }

    [Fact]
    public void KnownIds_AreCaseInsensitiveAndNormalizedToCanonicalSpelling()
    {
        var config = Parse(BaseJson.Replace(
            "{0}",
            ",\n  \"appearance\": { \"theme\": \"HIGHCONTRAST\", \"iconPack\": \"Filled\" }",
            StringComparison.Ordinal));

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal("highContrast", config.Appearance.Theme);
        Assert.Equal("filled", config.Appearance.IconPack);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code.StartsWith("config.appearance", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"#12\"")]
    [InlineData("\"not-a-color\"")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void InvalidOrBlankAccentColor_FallsBackToNullWithAWarning(string accentJson)
    {
        var config = Parse(BaseJson.Replace(
            "{0}",
            $",\n  \"appearance\": {{ \"accentColor\": {accentJson} }}",
            StringComparison.Ordinal));

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Null(config.Appearance.AccentColor);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.appearance.accentColor");
    }

    [Fact]
    public void ValidAccentColor_IsNormalizedToUppercase()
    {
        var config = Parse(BaseJson.Replace(
            "{0}",
            ",\n  \"appearance\": { \"accentColor\": \"#aabbcc\" }",
            StringComparison.Ordinal));

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal("#AABBCC", config.Appearance.AccentColor);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.appearance.accentColor");
    }

    [Fact]
    public void EightDigitAccentColor_IsPreserved()
    {
        var config = Parse(BaseJson.Replace(
            "{0}",
            ",\n  \"appearance\": { \"accentColor\": \"#80112233\" }",
            StringComparison.Ordinal));

        ConfigValidator.Validate(config);

        Assert.Equal("#80112233", config.Appearance.AccentColor);
    }

    private static PanelConfig Parse(string json) =>
        JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
}
