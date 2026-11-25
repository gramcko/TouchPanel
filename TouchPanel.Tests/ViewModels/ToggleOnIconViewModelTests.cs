using System.Windows.Media;
using TouchPanel.Config;
using TouchPanel.Services.Appearance;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// User-requested toggle on-state icon: the toggle projects one display-icon surface that follows
/// <see cref="ToggleWidgetViewModel.IsOn"/>. With no on icon it mirrors the base icon exactly; with
/// one it swaps glyph/vector + stroke and uses the configured on-icon colour (or the contrasting
/// theme default) only while on. It re-resolves on config apply and live icon-pack changes without
/// a rebuild.
/// </summary>
public sealed class ToggleOnIconViewModelTests
{
    [Fact]
    public async Task UnsetOnIcon_KeepsTheBaseIconInBothStates()
    {
        var viewModel = new ToggleWidgetViewModel(CreateConfig(initialOn: false, icon: "power"));

        Assert.False(viewModel.HasOnIcon);
        Assert.False(viewModel.UsesOnIcon);
        Assert.Equal(viewModel.IconGlyph, viewModel.DisplayIconGlyph);
        Assert.Equal(viewModel.IconPathData, viewModel.DisplayIconPathData);
        Assert.True(viewModel.HasDisplayIcon);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsOn);
        Assert.False(viewModel.UsesOnIcon);
        Assert.Equal(viewModel.IconGlyph, viewModel.DisplayIconGlyph);
        Assert.Null(viewModel.DisplayIconBrush);
        Assert.Equal(IconCatalog.GlyphFor("power"), viewModel.DisplayIconGlyph);
    }

    [Fact]
    public async Task UnsetOnIcon_DoesNotNotifyTheDisplayIconWhenTheStateFlips()
    {
        var viewModel = new ToggleWidgetViewModel(CreateConfig(initialOn: false, icon: "power"));
        var changes = RecordChanges(viewModel);

        await viewModel.ActivateAsync();

        Assert.Contains(nameof(ToggleWidgetViewModel.IsOn), changes);
        Assert.DoesNotContain(nameof(ToggleWidgetViewModel.DisplayIconGlyph), changes);
        Assert.DoesNotContain(nameof(ToggleWidgetViewModel.UsesOnIcon), changes);
    }

    [Fact]
    public async Task GlyphOnIcon_SwitchesWithTheOnState()
    {
        var viewModel = new ToggleWidgetViewModel(
            CreateConfig(initialOn: false, icon: "power", onIcon: "star"));

        Assert.True(viewModel.HasOnIcon);
        Assert.False(viewModel.UsesOnIcon);
        Assert.Equal(IconCatalog.GlyphFor("power"), viewModel.DisplayIconGlyph);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsOn);
        Assert.True(viewModel.UsesOnIcon);
        Assert.True(viewModel.HasDisplayIcon);
        Assert.Equal(IconCatalog.GlyphFor("star"), viewModel.DisplayIconGlyph);
        Assert.Null(viewModel.DisplayIconPathData);
        Assert.False(viewModel.DisplayIconStroke);

        await viewModel.ActivateAsync();

        Assert.False(viewModel.IsOn);
        Assert.False(viewModel.UsesOnIcon);
        Assert.Equal(IconCatalog.GlyphFor("power"), viewModel.DisplayIconGlyph);
    }

    [Fact]
    public void VectorAndStrokeOnIcon_SwitchesPathAndStrokeWithTheOnState()
    {
        var viewModel = new ToggleWidgetViewModel(
            CreateConfig(initialOn: true, icon: "power", onIcon: "microphoneMuted"));

        Assert.True(viewModel.IsOn);
        Assert.True(viewModel.UsesOnIcon);
        Assert.Null(viewModel.DisplayIconGlyph);
        Assert.Equal(IconCatalog.PathDataFor("microphoneMuted"), viewModel.DisplayIconPathData);
        Assert.True(viewModel.DisplayIconStroke);
        Assert.Equal(IconCatalog.LineArtStrokeUnits, viewModel.DisplayIconStrokeUnits);

        viewModel.ApplyConfig(CreateConfig(initialOn: false, icon: "power", onIcon: "microphoneMuted"));

        Assert.False(viewModel.IsOn);
        Assert.False(viewModel.UsesOnIcon);
        Assert.Equal(IconCatalog.GlyphFor("power"), viewModel.DisplayIconGlyph);
        Assert.Null(viewModel.DisplayIconPathData);
        Assert.False(viewModel.DisplayIconStroke);
        Assert.Equal(0, viewModel.DisplayIconStrokeUnits);
    }

    [Fact]
    public void FilledVectorOnIcon_ResolvesAsAnUnstrokedPath()
    {
        var viewModel = new ToggleWidgetViewModel(
            CreateConfig(initialOn: true, icon: "power", onIcon: "steam"));

        Assert.True(viewModel.UsesOnIcon);
        Assert.Null(viewModel.DisplayIconGlyph);
        Assert.Equal(IconCatalog.PathDataFor("steam"), viewModel.DisplayIconPathData);
        Assert.False(viewModel.DisplayIconStroke);
        Assert.Equal(0, viewModel.DisplayIconStrokeUnits);
        Assert.True(viewModel.HasDisplayIcon);
    }

    [Fact]
    public void OnIconFlag_IsOnlySetWhileOnWithAnOnIcon()
    {
        var withoutOnIcon = new ToggleWidgetViewModel(CreateConfig(initialOn: true, icon: "power"));
        Assert.False(withoutOnIcon.UsesOnIcon);

        var withOnIconOff = new ToggleWidgetViewModel(
            CreateConfig(initialOn: false, icon: "power", onIcon: "star"));
        Assert.False(withOnIconOff.UsesOnIcon);

        var withOnIconOn = new ToggleWidgetViewModel(
            CreateConfig(initialOn: true, icon: "power", onIcon: "star"));
        Assert.True(withOnIconOn.UsesOnIcon);
    }

    [Fact]
    public void DisplayIconBrush_UsesTheOnIconColourOnlyWhileOnAndFallsBackToForeground()
    {
        var viewModel = new ToggleWidgetViewModel(new ToggleWidgetConfig
        {
            Id = "toggle-1",
            Width = 100,
            Height = 50,
            Icon = "power",
            OnIcon = "star",
            OnIconColor = "#FF00FF00",
            InitialOn = false,
            Accent = "#FFFF00FF",
            Foreground = "#FF112233",
        });

        Assert.Same(viewModel.ForegroundBrush, viewModel.DisplayIconBrush);

        viewModel.ApplyConfig(new ToggleWidgetConfig
        {
            Id = "toggle-1",
            Width = 100,
            Height = 50,
            Icon = "power",
            OnIcon = "star",
            OnIconColor = "#FF00FF00",
            InitialOn = true,
            Accent = "#FFFF00FF",
            Foreground = "#FF112233",
        });

        Assert.True(viewModel.IsOn);
        Assert.Same(viewModel.OnIconBrush, viewModel.DisplayIconBrush);
        Assert.NotSame(viewModel.AccentBrush, viewModel.DisplayIconBrush);

        // No per-widget on-icon colour: null lets the template use the contrasting PanelBackgroundBrush.
        var plain = new ToggleWidgetViewModel(
            CreateConfig(initialOn: true, icon: "power", onIcon: "star"));
        Assert.Null(plain.OnIconBrush);
        Assert.Null(plain.DisplayIconBrush);
    }

    [Fact]
    public void OnIconColourChange_AppliesLiveAndNotifiesTheDisplayBrush()
    {
        var viewModel = new ToggleWidgetViewModel(
            CreateConfig(initialOn: true, icon: "power", onIcon: "star"));
        var changes = RecordChanges(viewModel);

        Assert.Null(viewModel.OnIconBrush);
        Assert.Null(viewModel.DisplayIconBrush);

        viewModel.ApplyConfig(new ToggleWidgetConfig
        {
            Id = "toggle-1",
            Width = 100,
            Height = 50,
            Icon = "power",
            OnIcon = "star",
            OnIconColor = "#FF445566",
            InitialOn = true,
        });

        Assert.Equal(Color.FromRgb(0x44, 0x55, 0x66), (viewModel.OnIconBrush as SolidColorBrush)?.Color);
        Assert.Same(viewModel.OnIconBrush, viewModel.DisplayIconBrush);
        Assert.Contains(nameof(ToggleWidgetViewModel.OnIconBrush), changes);
        Assert.Contains(nameof(ToggleWidgetViewModel.DisplayIconBrush), changes);

        // Clearing the colour returns to the theme default (null), still in the on state.
        viewModel.ApplyConfig(CreateConfig(initialOn: true, icon: "power", onIcon: "star"));

        Assert.Null(viewModel.OnIconBrush);
        Assert.Null(viewModel.DisplayIconBrush);
    }

    [Fact]
    public void OnIcon_ReResolvesOnALiveIconPackChange()
    {
        var state = new AppearanceState();
        using var viewModel = new ToggleWidgetViewModel(
            CreateConfig(initialOn: true, icon: "power", onIcon: "star"));
        viewModel.AttachAppearanceSource(state);

        Assert.Equal(IconCatalog.GlyphFor("star"), viewModel.DisplayIconGlyph);

        state.SetIconPack(IconPackIds.Filled);

        Assert.Equal(IconPackCatalog.GlyphFor(IconPackIds.Filled, "star"), viewModel.DisplayIconGlyph);
        Assert.NotEqual(IconCatalog.GlyphFor("star"), viewModel.DisplayIconGlyph);
        Assert.Equal(IconPackCatalog.GlyphFor(IconPackIds.Filled, "power"), viewModel.IconGlyph);
    }

    [Fact]
    public void ApplyConfig_ReResolvesAndClearsTheOnIcon_WithoutARebuild()
    {
        var viewModel = new ToggleWidgetViewModel(
            CreateConfig(initialOn: true, icon: "power", onIcon: "star"));
        var changes = RecordChanges(viewModel);

        viewModel.ApplyConfig(CreateConfig(initialOn: true, icon: "power", onIcon: "heart"));

        Assert.Equal(IconCatalog.GlyphFor("heart"), viewModel.DisplayIconGlyph);
        Assert.Contains(nameof(ToggleWidgetViewModel.DisplayIconGlyph), changes);
        Assert.Contains(nameof(ToggleWidgetViewModel.OnIconGlyph), changes);

        viewModel.ApplyConfig(CreateConfig(initialOn: true, icon: "power", onIcon: null));

        Assert.False(viewModel.HasOnIcon);
        Assert.False(viewModel.UsesOnIcon);
        Assert.Equal(IconCatalog.GlyphFor("power"), viewModel.DisplayIconGlyph);
        Assert.Contains(nameof(ToggleWidgetViewModel.HasOnIcon), changes);
        Assert.Contains(nameof(ToggleWidgetViewModel.UsesOnIcon), changes);
    }

    [Fact]
    public void ApplyConfig_WithAnUnchangedOnIconRaisesNoDisplayNotifications()
    {
        var config = CreateConfig(initialOn: true, icon: "power", onIcon: "star");
        var viewModel = new ToggleWidgetViewModel(config);
        var changes = RecordChanges(viewModel);

        viewModel.ApplyConfig(config);

        Assert.DoesNotContain(nameof(ToggleWidgetViewModel.DisplayIconGlyph), changes);
        Assert.DoesNotContain(nameof(ToggleWidgetViewModel.DisplayIconPathData), changes);
        Assert.DoesNotContain(nameof(ToggleWidgetViewModel.HasDisplayIcon), changes);
        Assert.DoesNotContain(nameof(ToggleWidgetViewModel.HasOnIcon), changes);
        Assert.DoesNotContain(nameof(ToggleWidgetViewModel.UsesOnIcon), changes);
        Assert.DoesNotContain(nameof(ToggleWidgetViewModel.DisplayIconBrush), changes);
    }

    private static List<string> RecordChanges(ToggleWidgetViewModel viewModel)
    {
        var changes = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is { } name)
            {
                changes.Add(name);
            }
        };
        return changes;
    }

    private static ToggleWidgetConfig CreateConfig(
        bool initialOn,
        string? icon = null,
        string? onIcon = null) => new()
    {
        Id = "toggle-1",
        X = 0,
        Y = 0,
        Width = 100,
        Height = 50,
        Label = "Mic",
        Icon = icon,
        OnIcon = onIcon,
        InitialOn = initialOn,
    };
}
