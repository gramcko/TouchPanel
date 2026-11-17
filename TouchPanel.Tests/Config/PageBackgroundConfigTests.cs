using System.IO;
using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Validation;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Optional per-page background (user-requested addition, additive under schema v4): defaults,
/// JSON round-trip, older files without the fields, and validation of colors and image paths.
/// Invalid values are cleared with a warning while the page stays usable; no schema bump.
/// </summary>
public sealed class PageBackgroundConfigTests
{
    [Fact]
    public void PageConfig_DefaultsToNoBackground()
    {
        var page = new PageConfig { Id = "main" };

        Assert.Null(page.Background);
        Assert.Null(page.BackgroundImage);
        Assert.Null(new PageConfig { Id = "main", Background = "   ", BackgroundImage = "  " }.Background);
        Assert.Null(new PageConfig { Id = "main", BackgroundImage = "  " }.BackgroundImage);
    }

    [Fact]
    public void JsonRoundTrip_PreservesTheBackgroundFields()
    {
        var config = Config(new PageConfig
        {
            Id = "main",
            Background = "#11223344",
            BackgroundImage = @"C:\images\wall.png",
        });

        var json = JsonSerializer.Serialize(config, ConfigJson.Options);
        var parsed = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options);

        var page = Assert.Single(parsed!.Pages);
        Assert.Equal("#11223344", page.Background);
        Assert.Equal(@"C:\images\wall.png", page.BackgroundImage);
        Assert.Equal(PanelConfig.CurrentSchemaVersion, parsed.SchemaVersion);
    }

    [Fact]
    public void OlderFile_WithoutBackgroundFields_LoadsUnchanged()
    {
        const string json = """
            {
              "schemaVersion": 4,
              "activePageId": "main",
              "pages": [
                { "id": "main", "name": "Main", "widgets": [] }
              ]
            }
            """;

        var parsed = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options);
        var diagnostics = ConfigValidator.Validate(parsed!);

        var page = Assert.Single(parsed!.Pages);
        Assert.Null(page.Background);
        Assert.Null(page.BackgroundImage);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code.StartsWith("config.page.background", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("#aabbcc", "#AABBCC")]
    [InlineData("  #11223344 ", "#11223344")]
    public void Validation_NormalizesAValidColor(string configured, string expected)
    {
        var page = new PageConfig { Id = "main", Background = configured };
        var config = Config(page);

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Equal(expected, page.Background);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "config.page.background");
    }

    [Fact]
    public void Validation_InvalidColor_IsClearedWithAWarningAndThePageStaysUsable()
    {
        var page = new PageConfig { Id = "main", Background = "red" };
        var config = Config(page);

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Null(page.Background);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.page.background");
        Assert.Equal("main", config.ActivePageId);
        Assert.Single(config.Pages);
    }

    [Fact]
    public async Task Validation_ExistingDecodableImage_IsKept()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = TestPng.Write(temp.DirectoryPath, "wall.png");
            var page = new PageConfig { Id = "main", BackgroundImage = path };
            var config = Config(page);

            var diagnostics = ConfigValidator.Validate(config);

            return (Kept: string.Equals(page.BackgroundImage, path, StringComparison.Ordinal), Diagnostics: diagnostics);
        });

        Assert.True(result.Kept);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "config.page.backgroundImage");
    }

    [Fact]
    public void Validation_RelativeImagePath_IsClearedWithAWarning()
    {
        var page = new PageConfig { Id = "main", BackgroundImage = @"images\wall.png" };
        var config = Config(page);

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Null(page.BackgroundImage);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.page.backgroundImage");
    }

    [Fact]
    public void Validation_MissingImage_IsClearedWithAWarning()
    {
        var missing = Path.Combine(Path.GetTempPath(), "touchpanel-missing-" + Guid.NewGuid().ToString("N") + ".png");
        var page = new PageConfig { Id = "main", BackgroundImage = missing };
        var config = Config(page);

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Null(page.BackgroundImage);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.page.backgroundImage");
    }

    [Fact]
    public void Validation_NonImageFile_IsClearedWithAWarning()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.DirectoryPath, "not-an-image.png");
        File.WriteAllText(path, "this is not an image");
        var page = new PageConfig { Id = "main", BackgroundImage = path };
        var config = Config(page);

        var diagnostics = ConfigValidator.Validate(config);

        Assert.Null(page.BackgroundImage);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "config.page.backgroundImage");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalizeImagePath_BlankMeansUnset(string? path)
    {
        Assert.True(PageBackground.TryNormalizeImagePath(path, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void Rename_PreservesTheBackground()
    {
        var pages = new[]
        {
            new PageConfig { Id = "main", Background = "#112233", BackgroundImage = @"C:\a.png" },
        };

        Assert.True(PageListOperations.TryRenamePage(pages, "main", "Renamed", out var updated, out _));

        var page = Assert.Single(updated);
        Assert.Equal("Renamed", page.Name);
        Assert.Equal("#112233", page.Background);
        Assert.Equal(@"C:\a.png", page.BackgroundImage);
    }

    [Fact]
    public void TrySetPageBackground_ChangesOnlyTheTargetPage()
    {
        var widgets = new List<WidgetConfig> { new LabelWidgetConfig { Id = "l", Width = 100, Height = 40 } };
        var pages = new[]
        {
            new PageConfig { Id = "main", Name = "Main", Widgets = widgets },
            new PageConfig { Id = "second", Name = "Second" },
        };

        Assert.True(PageListOperations.TrySetPageBackground(pages, "second", "#102030", @"C:\b.png", out var updated));

        Assert.Equal(2, updated.Count);
        Assert.Equal("main", updated[0].Id);
        Assert.Null(updated[0].Background);
        Assert.Same(widgets, updated[0].Widgets);
        Assert.Equal("Second", updated[1].Name);
        Assert.Equal("#102030", updated[1].Background);
        Assert.Equal(@"C:\b.png", updated[1].BackgroundImage);
    }

    [Fact]
    public void TrySetPageBackground_UnknownPage_LeavesTheListUnchanged()
    {
        var pages = new[] { new PageConfig { Id = "main" } };

        Assert.False(PageListOperations.TrySetPageBackground(pages, "missing", "#112233", null, out var updated));

        Assert.Same(pages, updated);
    }

    private static PanelConfig Config(params PageConfig[] pages) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = pages.Length > 0 ? pages[0].Id : null,
        Pages = [.. pages],
    };
}
