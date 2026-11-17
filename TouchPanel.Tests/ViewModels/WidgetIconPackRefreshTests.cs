using TouchPanel.Config;
using TouchPanel.Services.Appearance;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// M3-RF-844/M3-RF-845 coverage for the live icon-pack wiring: a widget view model resolves
/// <c>IconGlyph</c> through the active pack, refreshes it when the pack changes without a rebuild,
/// falls back to the default glyph for an unknown pack, and detaches on disposal. The registry
/// attaches the shared appearance source to every created widget.
/// </summary>
public sealed class WidgetIconPackRefreshTests
{
    [Fact]
    public void IconGlyph_ResolvesThroughTheActivePack_AndRefreshesLive()
    {
        var state = new AppearanceState();
        using var viewModel = new ButtonWidgetViewModel(new ButtonWidgetConfig
        {
            Id = "w1",
            Icon = "star",
            Width = 160,
            Height = 60,
        });

        viewModel.AttachAppearanceSource(state);
        Assert.Equal(IconCatalog.GlyphFor("star"), viewModel.IconGlyph);

        state.SetIconPack(IconPackIds.Filled);

        Assert.Equal(IconPackCatalog.GlyphFor(IconPackIds.Filled, "star"), viewModel.IconGlyph);
        Assert.NotEqual(IconCatalog.GlyphFor("star"), viewModel.IconGlyph);
    }

    [Theory]
    [InlineData(IconPackIds.Outline)]
    [InlineData(IconPackIds.Duotone)]
    public void IconGlyph_RefreshesLiveAcrossTheNewPacks(string packId)
    {
        var state = new AppearanceState();
        using var viewModel = new ButtonWidgetViewModel(new ButtonWidgetConfig
        {
            Id = "w6",
            Icon = "star",
            Width = 160,
            Height = 60,
        });

        viewModel.AttachAppearanceSource(state);
        var defaultGlyph = IconCatalog.GlyphFor("star");
        Assert.Equal(defaultGlyph, viewModel.IconGlyph);

        state.SetIconPack(packId);
        Assert.Equal(packId, state.ActiveIconPackId);
        Assert.Equal(IconPackCatalog.GlyphFor(packId, "star"), viewModel.IconGlyph);
        Assert.NotEqual(defaultGlyph, viewModel.IconGlyph);

        // Back to default: the same view model re-resolves without a rebuild.
        state.SetIconPack(IconPackIds.Default);
        Assert.Equal(defaultGlyph, viewModel.IconGlyph);
    }

    [Theory]
    [InlineData(IconPackIds.Outline)]
    [InlineData(IconPackIds.Duotone)]
    public void ChangingFromANewPackToAnUnknownPack_FallsBackToDefaultGlyph(string packId)
    {
        var state = new AppearanceState();
        using var viewModel = new ButtonWidgetViewModel(new ButtonWidgetConfig
        {
            Id = "w7",
            Icon = "star",
            Width = 160,
            Height = 60,
        });

        viewModel.AttachAppearanceSource(state);
        state.SetIconPack(packId);
        state.SetIconPack("hand-drawn");

        Assert.Equal(IconPackIds.Default, state.ActiveIconPackId);
        Assert.Equal(IconCatalog.GlyphFor("star"), viewModel.IconGlyph);
    }

    [Fact]
    public void ChangingToAnUnknownPack_FallsBackToDefaultGlyph()
    {
        var state = new AppearanceState();
        using var viewModel = new ButtonWidgetViewModel(new ButtonWidgetConfig
        {
            Id = "w2",
            Icon = "camera",
            Width = 160,
            Height = 60,
        });

        viewModel.AttachAppearanceSource(state);
        state.SetIconPack("hand-drawn");

        Assert.Equal(IconCatalog.GlyphFor("camera"), viewModel.IconGlyph);
        Assert.Equal(IconPackIds.Default, state.ActiveIconPackId);
    }

    [Fact]
    public void Dispose_DetachesFromThePackSource()
    {
        var state = new AppearanceState();
        var viewModel = new ButtonWidgetViewModel(new ButtonWidgetConfig
        {
            Id = "w3",
            Icon = "heart",
            Width = 160,
            Height = 60,
        });

        viewModel.AttachAppearanceSource(state);
        var before = viewModel.IconGlyph;
        Assert.NotNull(before);

        viewModel.Dispose();
        state.SetIconPack(IconPackIds.Filled);

        Assert.Equal(before, viewModel.IconGlyph);
    }

    [Fact]
    public void ReAttaching_DetachesThePreviousSource()
    {
        var first = new AppearanceState();
        var second = new AppearanceState();
        using var viewModel = new ButtonWidgetViewModel(new ButtonWidgetConfig
        {
            Id = "w4",
            Icon = "star",
            Width = 160,
            Height = 60,
        });

        viewModel.AttachAppearanceSource(first);
        viewModel.AttachAppearanceSource(second);

        // A change on the detached source no longer reaches the widget.
        first.SetIconPack(IconPackIds.Filled);
        Assert.Equal(IconCatalog.GlyphFor("star"), viewModel.IconGlyph);

        // The newly attached source does.
        second.SetIconPack(IconPackIds.Filled);
        Assert.Equal(IconPackCatalog.GlyphFor(IconPackIds.Filled, "star"), viewModel.IconGlyph);
    }

    [Fact]
    public void Registry_AttachesTheSharedAppearanceSourceToCreatedWidgets()
    {
        var state = new AppearanceState();
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext { AppearanceSource = state });

        using var viewModel = registry.Create(new ButtonWidgetConfig
        {
            Id = "w5",
            Icon = "star",
            Width = 160,
            Height = 60,
        });

        Assert.Equal(IconCatalog.GlyphFor("star"), viewModel.IconGlyph);

        state.SetIconPack(IconPackIds.Filled);
        Assert.Equal(IconPackCatalog.GlyphFor(IconPackIds.Filled, "star"), viewModel.IconGlyph);
    }
}
