using System.Windows;
using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Config;

/// <summary>
/// User-requested catalog expansion: the action/object MDL2 glyphs and the line-art vector icons are
/// additive after the original catalog (and brand vectors), every glyph is present in the installed
/// "Segoe MDL2 Assets" font (no tofu), every vector parses, stroke icons declare a positive stroke
/// fraction, and all new ids resolve identically in every icon pack.
/// </summary>
public sealed class ExpandedIconCatalogTests
{
    private static readonly (string Id, string Glyph)[] ActionGlyphs =
    [
        ("headset", "\uE95B"),
        ("volumeLow", "\uE993"),
        ("volumeMedium", "\uE994"),
        ("volumeHigh", "\uE995"),
        ("microphoneOff", "\uEC54"),
        ("brightness", "\uE706"),
        ("fullscreen", "\uE740"),
        ("restoreWindow", "\uE923"),
        ("sleep", "\uE708"),
        ("restart", "\uF83E"),
        ("unlock", "\uE785"),
        ("key", "\uE8D7"),
        ("shield", "\uEA18"),
        ("ethernet", "\uE839"),
        ("usb", "\uE88E"),
        ("hardDrive", "\uEDA2"),
        ("print", "\uE749"),
        ("screenshot", "\uF7ED"),
        ("undo", "\uE7A7"),
        ("redo", "\uE7A6"),
        ("copy", "\uE8C8"),
        ("paste", "\uE77F"),
        ("cut", "\uE8C6"),
        ("zoomIn", "\uE8A3"),
        ("zoomOut", "\uE71F"),
        ("filter", "\uE71C"),
        ("sort", "\uE8CB"),
        ("calculator", "\uE8EF"),
        ("code", "\uE943"),
        ("chat", "\uE8F2"),
        ("people", "\uE716"),
        ("phone", "\uE717"),
        ("videoCall", "\uE8AA"),
        ("photos", "\uE91B"),
        ("cart", "\uE7BF"),
    ];

    private static readonly string[] LineArtIds =
    [
        "headphones",
        "headphonesMuted",
        "microphoneMuted",
    ];

    private static readonly string[] BrandIds =
    [
        "steam", "discord", "whatsapp", "spotify", "chrome",
        "edge", "brave", "firefox", "vscode", "github",
    ];

    [Fact]
    public void Catalog_AppendsTheActionGroupAndLineArtAfterTheExistingEntries()
    {
        // 45 original glyphs + 10 brand vectors + 35 action glyphs + 3 line-art vectors.
        Assert.Equal(93, IconCatalog.BuiltIn.Count);
        Assert.InRange(IconCatalog.BuiltIn.Count, 1, 96);

        // Existing entries keep their position: the catalog still starts with power and the brand
        // group is untouched at indices 45-54.
        Assert.Equal("power", IconCatalog.BuiltIn[0].Id);
        Assert.Equal(BrandIds, IconCatalog.BuiltIn.Skip(45).Take(10).Select(option => option.Id));

        // New groups follow, glyphs first and line art last.
        Assert.Equal(
            ActionGlyphs.Select(entry => entry.Id),
            IconCatalog.BuiltIn.Skip(55).Take(ActionGlyphs.Length).Select(option => option.Id));
        Assert.Equal(
            LineArtIds,
            IconCatalog.BuiltIn.Skip(IconCatalog.BuiltIn.Count - LineArtIds.Length).Select(option => option.Id));

        Assert.Equal(80, IconCatalog.BuiltIn.Count(option => option.HasGlyph));
        Assert.Equal(13, IconCatalog.BuiltIn.Count(option => option.IsVector));
    }

    [Fact]
    public void ActionGlyphs_AreExactMdl2EntriesWithoutPaths()
    {
        foreach (var (id, glyph) in ActionGlyphs)
        {
            Assert.True(IconCatalog.TryGet(id, out var option), $"Missing action glyph '{id}'.");
            Assert.Equal(glyph, option.Glyph);
            Assert.True(option.HasGlyph);
            Assert.False(option.IsVector);
            Assert.Null(option.PathData);
            Assert.False(option.Stroke);
            Assert.Equal(0, option.StrokeUnits);
            Assert.False(string.IsNullOrWhiteSpace(option.DisplayName));
        }
    }

    [Fact]
    public void LineArtVectors_AreStrokedWithAPositiveFraction()
    {
        foreach (var id in LineArtIds)
        {
            Assert.True(IconCatalog.TryGet(id, out var option), $"Missing line-art icon '{id}'.");
            Assert.False(option.HasGlyph);
            Assert.True(option.IsVector);
            Assert.True(option.Stroke, $"Line-art icon '{id}' must declare Stroke=true.");
            Assert.Equal(IconCatalog.LineArtStrokeUnits, option.StrokeUnits);
            Assert.True(option.StrokeFraction > 0, $"Line-art icon '{id}' must declare a positive stroke fraction.");
            Assert.Equal(2d / 24d, option.StrokeFraction, precision: 6);
            Assert.False(string.IsNullOrWhiteSpace(option.PathData));
            Assert.False(string.IsNullOrWhiteSpace(option.DisplayName));
        }
    }

    [Fact]
    public void EveryVectorPath_ParsesIntoNonEmptyGeometry_AndBrandsStayFilled()
    {
        foreach (var option in IconCatalog.BuiltIn.Where(option => option.IsVector))
        {
            var geometry = Geometry.Parse(option.PathData!);
            Assert.False(geometry.IsEmpty(), $"Icon '{option.Id}' parsed to an empty geometry.");
            Assert.False(geometry.Bounds.IsEmpty, $"Icon '{option.Id}' has empty bounds.");
        }

        foreach (var id in BrandIds)
        {
            Assert.True(IconCatalog.TryGet(id, out var option));
            Assert.False(option.Stroke, $"Brand icon '{id}' must remain a filled vector.");
            Assert.Equal(0, option.StrokeUnits);
            Assert.Equal(0, option.StrokeFraction);
            Assert.False(option.HasGlyph);
        }
    }

    [Mdl2FontFact]
    public void FontCoverage_EveryGlyphExistsInTheInstalledMdl2Font()
    {
        var typeface = new Typeface(
            new FontFamily("Segoe MDL2 Assets"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);
        Assert.True(
            typeface.TryGetGlyphTypeface(out var glyphTypeface),
            "Segoe MDL2 Assets could not be loaded despite being installed.");

        foreach (var option in IconCatalog.BuiltIn.Where(option => option.HasGlyph))
        {
            var codepoint = option.Glyph[0];
            Assert.True(
                glyphTypeface.CharacterToGlyphMap.TryGetValue(codepoint, out var glyphIndex) && glyphIndex != 0,
                $"Icon '{option.Id}' (U+{(int)codepoint:X4}) has no glyph in Segoe MDL2 Assets (tofu).");
        }
    }

    [Fact]
    public void NewIds_ResolveIdenticallyInEveryPack()
    {
        string?[] packs = [null, IconPackIds.Default, IconPackIds.Filled, IconPackIds.Outline, IconPackIds.Duotone];

        foreach (var (id, glyph) in ActionGlyphs)
        {
            foreach (var pack in packs)
            {
                Assert.True(WidgetIconResolver.TryResolve(pack, id, out var visual), $"Pack '{pack}' did not resolve '{id}'.");
                Assert.Equal(glyph, visual.Glyph);
                Assert.Null(visual.PathData);
                Assert.False(visual.Stroke);
                Assert.Equal(glyph, IconPackCatalog.GlyphFor(pack, id));
            }
        }

        foreach (var id in LineArtIds)
        {
            var expectedPath = IconCatalog.PathDataFor(id);
            Assert.NotNull(expectedPath);

            foreach (var pack in packs)
            {
                Assert.True(WidgetIconResolver.TryResolve(pack, id, out var visual), $"Pack '{pack}' did not resolve '{id}'.");
                Assert.Null(visual.Glyph);
                Assert.Equal(expectedPath, visual.PathData);
                Assert.True(visual.Stroke);
                Assert.Equal(IconCatalog.LineArtStrokeUnits, visual.StrokeUnits);
                Assert.True(visual.IsVector);
                Assert.False(IconPackCatalog.TryGetGlyph(pack, id, out _));
            }
        }
    }

    [Fact]
    public void UnknownIds_StillFailWithNoVisual()
    {
        foreach (var pack in IconPackIds.All)
        {
            Assert.False(WidgetIconResolver.TryResolve(pack, "not-a-real-icon", out var visual));
            Assert.False(visual.HasIcon);
            Assert.False(IconPackCatalog.TryGetGlyph(pack, "not-a-real-icon", out _));
        }

        Assert.False(IconCatalog.TryGet("headphonez", out _));
        Assert.Null(IconCatalog.GlyphFor("headphonez"));
        Assert.Null(IconCatalog.PathDataFor("headphonez"));
    }
}
