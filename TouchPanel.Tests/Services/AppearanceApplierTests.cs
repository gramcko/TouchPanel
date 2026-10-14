using TouchPanel.Config;
using TouchPanel.Services.Appearance;
using TouchPanel.Theme;

namespace TouchPanel.Tests.Services;

/// <summary>
/// M3-RF-848/F-M3-16 coverage for the atomic appearance apply: the theme/accent swap and the icon-pack
/// publish are treated as one wholesale operation. The pack is published only when the theme/accent
/// apply succeeded, so a failed apply never leaves the old theme/accent combined with the new pack.
/// </summary>
public sealed class AppearanceApplierTests
{
    [Fact]
    public void Apply_Success_PublishesTheIconPack()
    {
        var state = new AppearanceState();
        string? appliedTheme = null;
        string? appliedAccent = null;
        var applier = new AppearanceApplier(
            (theme, accent) =>
            {
                appliedTheme = theme;
                appliedAccent = accent;
                return true;
            },
            state);

        var result = applier.Apply(new AppearanceConfig
        {
            Theme = ThemeIds.Light,
            AccentColor = "#112233",
            IconPack = IconPackIds.Filled,
        });

        Assert.True(result);
        Assert.Equal(ThemeIds.Light, appliedTheme);
        Assert.Equal("#112233", appliedAccent);
        Assert.Equal(IconPackIds.Filled, state.ActiveIconPackId);
    }

    [Fact]
    public void Apply_ThemeFailure_DoesNotPublishTheIconPack()
    {
        var state = new AppearanceState();
        var raised = 0;
        state.AppearanceChanged += (_, _) => raised++;

        var applier = new AppearanceApplier((_, _) => false, state);

        var result = applier.Apply(new AppearanceConfig { IconPack = IconPackIds.Filled });

        Assert.False(result);
        Assert.Equal(IconPackIds.Default, state.ActiveIconPackId);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Apply_ThemeFailure_KeepsThePreviouslyPublishedPack()
    {
        var state = new AppearanceState();
        state.SetIconPack(IconPackIds.Filled);

        var applier = new AppearanceApplier((_, _) => false, state);

        var result = applier.Apply(new AppearanceConfig { IconPack = IconPackIds.Default });

        Assert.False(result);

        // Wholesale: the previously active pack remains in effect, not the requested one.
        Assert.Equal(IconPackIds.Filled, state.ActiveIconPackId);
    }

    [Fact]
    public void Apply_SuccessWithoutASharedState_DoesNotThrow()
    {
        var applier = new AppearanceApplier((_, _) => true, appearanceState: null);

        var result = applier.Apply(new AppearanceConfig { IconPack = IconPackIds.Filled });

        Assert.True(result);
    }

    [Fact]
    public void Apply_NullAppearance_Throws()
    {
        var applier = new AppearanceApplier((_, _) => true, new AppearanceState());

        Assert.Throws<ArgumentNullException>(() => applier.Apply(null!));
    }

    [Fact]
    public void Constructor_NullApplyDelegate_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AppearanceApplier(null!, new AppearanceState()));
    }
}
