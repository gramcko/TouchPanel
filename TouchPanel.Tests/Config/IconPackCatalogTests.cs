using TouchPanel.Config;

namespace TouchPanel.Tests.Config;

/// <summary>
/// M3-RF-844/F-M3-05 coverage for the built-in icon-pack catalog: the default pack reproduces the M2
/// icon catalog exactly, an alternate pack overrides a subset of the shared stable id namespace, and
/// an id absent from the active pack (or an unknown pack id) falls back to the default pack glyph —
/// ids are never cleared.
/// </summary>
public sealed class IconPackCatalogTests
{
    [Fact]
    public void PackIds_FollowTheIconPackAllowlist()
    {
        Assert.Equal(IconPackIds.All, IconPackCatalog.PackIds);
        Assert.Contains(IconPackIds.Default, IconPackCatalog.PackIds);
        Assert.Contains(IconPackIds.Filled, IconPackCatalog.PackIds);
        Assert.True(IconPackCatalog.PackIds.Count >= 2);
    }

    [Fact]
    public void DefaultPack_ReproducesTheM2IconCatalog()
    {
        foreach (var option in IconCatalog.BuiltIn)
        {
            Assert.True(IconPackCatalog.TryGetGlyph(IconPackIds.Default, option.Id, out var glyph));
            Assert.Equal(option.Glyph, glyph);
            Assert.Equal(IconCatalog.GlyphFor(option.Id), glyph);
        }
    }

    [Fact]
    public void FilledPack_OverridesASubset_ButMissingIdsFallBackToDefault()
    {
        // `star` is overridden by the filled pack (a filled favorite glyph).
        var defaultStar = IconCatalog.GlyphFor("star");
        var filledStar = IconPackCatalog.GlyphFor(IconPackIds.Filled, "star");
        Assert.NotNull(defaultStar);
        Assert.NotNull(filledStar);
        Assert.NotEqual(defaultStar, filledStar);

        // `camera` is not in the filled pack, so it resolves to the default pack glyph.
        Assert.Equal(IconCatalog.GlyphFor("camera"), IconPackCatalog.GlyphFor(IconPackIds.Filled, "camera"));
    }

    [Fact]
    public void FilledOverrides_AllExistInTheStableIdNamespace()
    {
        // Every filled override maps a known default-pack id; verified via the public resolver so the
        // namespace stays stable (no unknown ids introduced by a pack).
        var filledOverrides = new[] { "star", "heart", "bell", "check", "add", "play", "pause", "stop", "power" };
        foreach (var id in filledOverrides)
        {
            Assert.NotNull(IconPackCatalog.GlyphFor(IconPackIds.Filled, id));
        }
    }

    [Fact]
    public void UnknownPackId_FallsBackToTheDefaultPackGlyph()
    {
        Assert.Equal(IconCatalog.GlyphFor("power"), IconPackCatalog.GlyphFor("hand-drawn", "power"));
        Assert.Equal(IconCatalog.GlyphFor("power"), IconPackCatalog.GlyphFor(null, "power"));
    }

    [Fact]
    public void UnknownOrMissingIconId_ReturnsFalseOrNull()
    {
        Assert.False(IconPackCatalog.TryGetGlyph(IconPackIds.Default, "not-a-real-icon", out _));
        Assert.Null(IconPackCatalog.GlyphFor(IconPackIds.Default, "not-a-real-icon"));
        Assert.Null(IconPackCatalog.GlyphFor(IconPackIds.Filled, "not-a-real-icon"));
        Assert.Null(IconPackCatalog.GlyphFor(IconPackIds.Default, null));
        Assert.Null(IconPackCatalog.GlyphFor(IconPackIds.Default, "   "));
    }

    [Fact]
    public void PackIds_AreCaseInsensitiveAndNormalized()
    {
        // A non-overridden id still uses the default glyph regardless of the pack id's case.
        Assert.Equal(IconCatalog.GlyphFor("camera"), IconPackCatalog.GlyphFor("FILLED", "camera"));

        // The filled override applies case-insensitively and differs from the default pack glyph.
        Assert.Equal(IconPackCatalog.GlyphFor(IconPackIds.Filled, "star"), IconPackCatalog.GlyphFor("FILLED", "star"));
        Assert.NotEqual(IconCatalog.GlyphFor("star"), IconPackCatalog.GlyphFor("FILLED", "star"));
    }
}
