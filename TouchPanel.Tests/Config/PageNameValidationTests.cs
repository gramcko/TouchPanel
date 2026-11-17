using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Validation;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Page-name normalization (user-requested rename): the additive <c>name</c> field stays schema v4,
/// blank values fall back to the id, over-long values are truncated with a diagnostic, and the name
/// round-trips through the canonical JSON.
/// </summary>
public sealed class PageNameValidationTests
{
    [Fact]
    public void Validate_TruncatesAnOverLongPageName_WithADiagnostic()
    {
        var config = ConfigWith(new PageConfig
        {
            Id = "main",
            Name = new string('p', ConfigValidator.MaxPageNameLength + 5),
        });

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal(ConfigValidator.MaxPageNameLength, config.Pages[0].Name!.Length);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.page.name");
    }

    [Fact]
    public void Validate_TrimsAValidPageName_WithoutAWarning()
    {
        var config = ConfigWith(new PageConfig { Id = "main", Name = "  Dashboard  " });

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal("Dashboard", config.Pages[0].Name);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.page.name");
    }

    [Fact]
    public void PageName_RoundTripsUnderSchemaV4_WithNoSchemaBump()
    {
        var config = ConfigWith(new PageConfig { Id = "main", Name = "Home" });

        var json = JsonSerializer.Serialize(config, ConfigJson.Options);
        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.Contains("\"name\": \"Home\"", json, StringComparison.Ordinal);
        Assert.Equal(4, restored.SchemaVersion);
        Assert.Equal("Home", restored.Pages[0].Name);
        Assert.Equal(4, PanelConfig.CurrentSchemaVersion);
    }

    [Fact]
    public void UnnamedPage_OmitsTheNameProperty_AndDisplaysItsId()
    {
        var config = ConfigWith(new PageConfig { Id = "main" });

        var json = JsonSerializer.Serialize(config, ConfigJson.Options);

        Assert.DoesNotContain("\"name\"", json, StringComparison.Ordinal);
        Assert.Equal("main", PageListOperations.DisplayName(config.Pages[0]));
    }

    private static PanelConfig ConfigWith(PageConfig page) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = page.Id,
        Pages = [page],
    };
}
