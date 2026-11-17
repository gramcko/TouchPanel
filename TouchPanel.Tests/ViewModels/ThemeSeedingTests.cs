using TouchPanel.Config;
using TouchPanel.Theme;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Theme-aware default seeding (M5-RF-541/M5-RF-543): a widget added under each theme receives that
/// theme's font size, opacity, and accent (font size/opacity normalized into bounds, colors valid)
/// while foreground/background stay inherited, so a later theme switch keeps restyling the widget. A
/// pre-existing widget with null appearance is never rewritten.
/// </summary>
public sealed class ThemeSeedingTests
{
    private const string PageId = "main";

    [Theory]
    [InlineData(ThemeIds.Dark)]
    [InlineData(ThemeIds.Light)]
    [InlineData(ThemeIds.HighContrast)]
    [InlineData(ThemeIds.Midnight)]
    [InlineData(ThemeIds.Nord)]
    [InlineData(ThemeIds.Oled)]
    public void AddedWidget_IsSeededFromTheActiveTheme(string themeId)
    {
        var context = CreateContext(themeId);
        context.Service.Enter(1000, 500);

        Assert.True(context.Service.AddWidget(WidgetTypes.Button));

        var config = context.Config.Pages[0].Widgets[0];
        var expected = ThemeAppearanceDefaults.For(themeId);

        Assert.Equal(WidgetAppearance.NormalizeFontSize(expected.FontSize), config.FontSize);
        Assert.Equal(WidgetAppearance.NormalizeOpacity(expected.Opacity), config.Opacity);
        Assert.Equal(WidgetAppearance.NormalizeColor(expected.Accent), config.Accent);

        // Q-M5-1: foreground/background stay inherited so a later theme switch keeps working.
        Assert.Null(config.Foreground);
        Assert.Null(config.Background);
    }

    [Fact]
    public void AddedWidget_SeededValuesAreValid()
    {
        var context = CreateContext(ThemeIds.HighContrast);
        context.Service.Enter(1000, 500);
        Assert.True(context.Service.AddWidget(WidgetTypes.Label));

        var config = context.Config.Pages[0].Widgets[0];
        Assert.True(WidgetAppearance.IsValidColor(config.Accent));
        Assert.InRange(config.FontSize!.Value, WidgetAppearance.MinFontSize, WidgetAppearance.MaxFontSize);
        Assert.InRange(config.Opacity!.Value, WidgetAppearance.MinOpacity, WidgetAppearance.MaxOpacity);
    }

    [Fact]
    public void PreExistingWidgetWithNullAppearance_IsNotRewrittenOnLoad()
    {
        var context = CreateContext(ThemeIds.Light, new LabelWidgetConfig
        {
            Id = "w1",
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Label = "existing",
        });

        context.Service.Enter(1000, 500);

        var config = context.Config.Pages[0].Widgets[0];
        Assert.Null(config.FontSize);
        Assert.Null(config.Opacity);
        Assert.Null(config.Accent);
    }

    [Fact]
    public void Editor_DisplaysTheActiveThemeDefaultForANullOverride()
    {
        var context = CreateContext(ThemeIds.Light);
        context.Service.Enter(1000, 500);
        Assert.True(context.Service.AddWidget(WidgetTypes.Label));
        var editor = context.Service.PropertyEditor!;

        // Clear the seeded overrides to simulate an inherited field.
        editor.FontSizeText = string.Empty;
        editor.OpacityText = string.Empty;

        Assert.Equal("15", editor.ThemeFontSizeText);
        Assert.Equal("1", editor.ThemeOpacityText);
        Assert.Equal(ThemeAppearanceDefaults.Light.Foreground, editor.ForegroundText);
    }

    private static TestContext CreateContext(string themeId, params WidgetConfig[] widgets)
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = PageId,
            Appearance = new AppearanceConfig { Theme = themeId },
            Pages = [new PageConfig { Id = PageId, Widgets = [.. widgets] }],
        };
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var store = new RecordingConfigStore();
        var service = new EditModeService(config, page, store, factory: registry, palette: registry);
        return new TestContext(config, page, store, service);
    }

    private sealed record TestContext(
        PanelConfig Config,
        PageViewModel Page,
        RecordingConfigStore Store,
        EditModeService Service);
}
