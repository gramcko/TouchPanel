using System.Text.Json;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Validation;

namespace TouchPanel.Tests.Config;

/// <summary>
/// User-requested brand icons: the ten simple-icons (CC0 1.0) vector ids are part of the built-in
/// catalog, resolve identically in every icon pack, persist unchanged through JSON and validation,
/// and keep the existing glyph entries (including download/upload) working.
/// </summary>
public sealed class BrandIconCatalogTests
{
    private static readonly string[] BrandIds =
    [
        "steam",
        "discord",
        "whatsapp",
        "spotify",
        "chrome",
        "edge",
        "brave",
        "firefox",
        "vscode",
        "github",
    ];

    [Fact]
    public void Catalog_ContainsExactlyTheTenBrandVectorsAndTheOriginalGlyphs()
    {
        Assert.Equal(93, IconCatalog.BuiltIn.Count);

        foreach (var id in BrandIds)
        {
            Assert.True(IconCatalog.TryGet(id, out var option), $"Missing brand icon '{id}'.");
            Assert.True(option.IsVector, $"Brand icon '{id}' must be a vector.");
            Assert.False(option.HasGlyph, $"Brand icon '{id}' must not carry an MDL2 glyph.");
            Assert.False(string.IsNullOrWhiteSpace(option.PathData));
            Assert.False(string.IsNullOrWhiteSpace(option.DisplayName));
        }

        // Existing glyph entries and the two most recent additions still work.
        Assert.True(IconCatalog.TryGet("download", out var download));
        Assert.Equal("\uE896", download.Glyph);
        Assert.True(IconCatalog.TryGet("upload", out var upload));
        Assert.Equal("\uE898", upload.Glyph);
        Assert.True(IconCatalog.BuiltIn.Count(option => option.HasGlyph) >= 45);
    }

    [Fact]
    public void BrandVectors_AreValidWpfGeometries()
    {
        foreach (var id in BrandIds)
        {
            var path = IconCatalog.PathDataFor(id);
            Assert.NotNull(path);
            var geometry = Geometry.Parse(path);
            Assert.False(geometry.IsEmpty(), $"Brand icon '{id}' parsed to an empty geometry.");
        }
    }

    [Fact]
    public void BrandVectors_ResolveIdenticallyInEveryPack()
    {
        string?[] packs = [null, IconPackIds.Default, IconPackIds.Filled, IconPackIds.Outline, IconPackIds.Duotone];

        foreach (var id in BrandIds)
        {
            var expectedPath = IconCatalog.PathDataFor(id);
            Assert.NotNull(expectedPath);

            foreach (var pack in packs)
            {
                Assert.True(WidgetIconResolver.TryResolve(pack, id, out var visual));
                Assert.Null(visual.Glyph);
                Assert.Equal(expectedPath, visual.PathData);
                Assert.True(visual.IsVector);
                Assert.Equal(expectedPath, WidgetIconResolver.ResolvePathData(pack, id));
                Assert.Null(WidgetIconResolver.ResolveGlyph(pack, id));
                Assert.False(IconPackCatalog.TryGetGlyph(pack, id, out _));
            }
        }
    }

    [Fact]
    public void GlyphIds_KeepThePackOverridesAndDefaultFallback()
    {
        Assert.Equal(IconCatalog.GlyphFor("star"), WidgetIconResolver.ResolveGlyph(IconPackIds.Default, "star"));
        Assert.Equal(
            IconPackCatalog.GlyphFor(IconPackIds.Filled, "star"),
            WidgetIconResolver.ResolveGlyph(IconPackIds.Filled, "star"));
        Assert.Equal("\uE896", WidgetIconResolver.ResolveGlyph(IconPackIds.Filled, "download"));
        Assert.Null(WidgetIconResolver.ResolvePathData(IconPackIds.Filled, "star"));
    }

    [Fact]
    public void Validation_PreservesBrandIdsAndClearsOnlyUnknownIds()
    {
        var widget = new LabelWidgetConfig
        {
            Id = "label",
            Width = 200,
            Height = 100,
            Label = "Apps",
            Icon = "github",
            LabelIcon = "steam",
        };

        var diagnostics = ConfigValidator.Validate(CreateConfig(widget));

        Assert.Equal("github", widget.Icon);
        Assert.Equal("steam", widget.LabelIcon);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code is "config.widget.icon" or "config.widget.labelIcon");

        widget.Icon = "not-a-real-icon";
        widget.LabelIcon = "also-not-real";

        var unknownDiagnostics = ConfigValidator.Validate(CreateConfig(widget));

        Assert.Null(widget.Icon);
        Assert.Null(widget.LabelIcon);
        Assert.Contains(unknownDiagnostics, diagnostic => diagnostic.Code == "config.widget.icon");
        Assert.Contains(unknownDiagnostics, diagnostic => diagnostic.Code == "config.widget.labelIcon");
    }

    [Fact]
    public void BrandIds_RoundTripThroughJsonUnchanged()
    {
        var config = CreateConfig(new LabelWidgetConfig
        {
            Id = "label",
            Width = 200,
            Height = 100,
            Icon = "vscode",
            LabelIcon = "edge",
        });

        var json = JsonSerializer.Serialize(config, ConfigJson.Options);
        var restored = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;
        var widget = Assert.IsType<LabelWidgetConfig>(restored.Pages[0].Widgets[0]);

        Assert.Equal("vscode", widget.Icon);
        Assert.Equal("edge", widget.LabelIcon);
    }

    private static PanelConfig CreateConfig(params WidgetConfig[] widgets) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = "main",
        Pages = [new PageConfig { Id = "main", Widgets = [.. widgets] }],
    };
}
