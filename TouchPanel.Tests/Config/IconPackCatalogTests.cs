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
        Assert.Contains(IconPackIds.Outline, IconPackCatalog.PackIds);
        Assert.Contains(IconPackIds.Duotone, IconPackCatalog.PackIds);
        Assert.True(IconPackCatalog.PackIds.Count >= 4);
    }

    [Theory]
    [InlineData(IconPackIds.Default)]
    [InlineData(IconPackIds.Filled)]
    [InlineData(IconPackIds.Outline)]
    [InlineData(IconPackIds.Duotone)]
    public void EveryPack_ResolvesEveryStableIconId(string packId)
    {
        foreach (var option in IconCatalog.BuiltIn)
        {
            Assert.True(
                IconPackCatalog.TryGetGlyph(packId, option.Id, out var glyph),
                $"Pack '{packId}' did not resolve stable id '{option.Id}'.");
            Assert.False(string.IsNullOrEmpty(glyph));
        }
    }

    [Fact]
    public void EveryPack_KeepsTheStableIdNamespace()
    {
        foreach (var packId in IconPackIds.All)
        {
            Assert.False(IconPackCatalog.TryGetGlyph(packId, "not-a-real-icon", out _));
            Assert.Null(IconPackCatalog.GlyphFor(packId, "not-a-real-icon"));
        }
    }

    [Theory]
    [InlineData(IconPackIds.Outline)]
    [InlineData(IconPackIds.Duotone)]
    public void NewPacks_OverrideASubset_ButMissingIdsFallBackToDefault(string packId)
    {
        var overriddenIds = new[] { "star", "heart" };
        foreach (var id in overriddenIds)
        {
            var defaultGlyph = IconCatalog.GlyphFor(id);
            var packGlyph = IconPackCatalog.GlyphFor(packId, id);
            Assert.NotNull(defaultGlyph);
            Assert.NotNull(packGlyph);
            Assert.NotEqual(defaultGlyph, packGlyph);
        }

        // `microphone` is intentionally not overridden by the new packs, so it resolves to default.
        Assert.Equal(IconCatalog.GlyphFor("microphone"), IconPackCatalog.GlyphFor(packId, "microphone"));
    }

    [Fact]
    public void NewPacks_AreCaseInsensitiveAndNormalized()
    {
        Assert.Equal(
            IconPackCatalog.GlyphFor(IconPackIds.Outline, "star"),
            IconPackCatalog.GlyphFor("OUTLINE", "star"));
        Assert.Equal(
            IconPackCatalog.GlyphFor(IconPackIds.Duotone, "star"),
            IconPackCatalog.GlyphFor("  Duotone  ", "star"));
        Assert.NotEqual(
            IconPackCatalog.GlyphFor(IconPackIds.Outline, "star"),
            IconPackCatalog.GlyphFor(IconPackIds.Duotone, "star"));
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
