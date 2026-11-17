using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TouchPanel.Tests.TestSupport;
using TouchPanel.Theme;
using TouchPanel.ViewModels.About;
using TouchPanel.Views.About;

namespace TouchPanel.Tests.Theme;

/// <summary>
/// User-requested default-button chrome fix (About-window hover contrast). The stock WPF Button
/// template replaces the themed Background with a light system hover brush, which made the light
/// themed label unreadable on hover in the dark palettes. These tests load the real
/// <c>Theme/Styles.xaml</c> (BAML) plus a real theme dictionary on an STA thread and assert:
/// <list type="bullet">
/// <item>the implicit <see cref="Button"/> style applies <c>ThemedButtonTemplate</c> (themed surface
/// and border, rounded corners, no stock chrome) in every built-in theme;</item>
/// <item>the hover trigger paints <c>WidgetPressedBrush</c> with the accent border, the pressed
/// trigger one step darker, and the disabled trigger the disabled surface/foreground;</item>
/// <item>the theme foreground stays readable on the hover and pressed surfaces in all six themes;</item>
/// <item>the About buttons (both the raised and the link style) build on that shared style, and a
/// hovered link switches to the theme foreground so the accent cannot fall below ~3:1;</item>
/// <item>the pre-existing explicit button styles keep their own themed templates (no regression to
/// the stock light hover chrome).</item>
/// </list>
/// No mouse automation: the hover state is asserted on the template triggers, exactly as authored.
/// </summary>
public sealed class ThemedButtonStyleTests
{
    [Theory]
    [InlineData(ThemeIds.Dark)]
    [InlineData(ThemeIds.Light)]
    [InlineData(ThemeIds.HighContrast)]
    [InlineData(ThemeIds.Midnight)]
    [InlineData(ThemeIds.Nord)]
    [InlineData(ThemeIds.Oled)]
    public async Task ImplicitButtonStyle_ReplacesStockChrome_WithTheThemeSurface(string themeId)
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var theme = LoadTheme(themeId);
            var styles = LoadStyles();
            var host = new Grid();
            host.Resources.MergedDictionaries.Add(theme);
            host.Resources.MergedDictionaries.Add(styles);

            var button = Realize(new Button { Content = "OK" }, host);

            // The implicit app-wide style is the effective style, and it carries the shared template.
            var implicitStyle = Assert.IsType<Style>(styles[typeof(Button)]);
            var themed = Assert.IsType<Style>(styles["ThemedButtonStyle"]);
            Assert.Same(implicitStyle, button.Style);
            Assert.Same(themed, implicitStyle.BasedOn);
            Assert.Same(TemplateOf(themed), button.Template);

            var surface = RequireTemplateBorder(button, "Surface");
            Assert.Equal(ColorOf(theme, "WidgetSurfaceBrush"), BrushColor(surface.Background));
            Assert.Equal(ColorOf(theme, "WidgetBorderBrush"), BrushColor(surface.BorderBrush));
            Assert.Equal(new CornerRadius(8), surface.CornerRadius);
            Assert.Equal(ColorOf(theme, "PanelForegroundBrush"), BrushColor(button.Foreground));

            // No stock chrome (the default template's ButtonChrome/themed border block) survives.
            Assert.DoesNotContain(
                FindDescendants<FrameworkElement>(button),
                element => element.GetType().Name.Contains("Chrome", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task HoverTrigger_UsesTheDarkerPressedSurfaceAndTheAccentBorder()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var themed = Assert.IsType<Style>(LoadStyles()["ThemedButtonStyle"]);
            var template = Assert.IsType<ControlTemplate>(TemplateOf(themed));

            var hover = RequireTrigger(template, UIElement.IsMouseOverProperty);
            Assert.Equal(
                "WidgetPressedBrush",
                ResourceKeyOf(RequireSetter(hover, "Surface", Panel.BackgroundProperty)));
            Assert.Equal(
                "PanelAccentBrush",
                ResourceKeyOf(RequireSetter(hover, "Surface", Border.BorderBrushProperty)));

            // The hover surface is the theme brush every palette defines (so no system color leaks).
            foreach (var themeId in ThemeIds.All)
            {
                Assert.True(
                    LoadTheme(themeId).Contains("WidgetPressedBrush"),
                    $"Theme '{themeId}' is missing the hover surface brush.");
            }
        });
    }

    [Fact]
    public async Task PressedAndDisabledTriggers_UseTheThemedStateBrushes()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var themed = Assert.IsType<Style>(LoadStyles()["ThemedButtonStyle"]);
            var template = Assert.IsType<ControlTemplate>(TemplateOf(themed));

            // Pressed: one step below the resting surface, accent border kept.
            var pressed = RequireTrigger(template, ButtonBase.IsPressedProperty);
            Assert.Equal(
                "WidgetDisabledBrush",
                ResourceKeyOf(RequireSetter(pressed, "Surface", Panel.BackgroundProperty)));
            Assert.Equal(
                "PanelAccentBrush",
                ResourceKeyOf(RequireSetter(pressed, "Surface", Border.BorderBrushProperty)));

            // Disabled: disabled surface/border and the disabled foreground, declared last so it
            // wins over hover/press.
            var disabled = RequireTrigger(template, UIElement.IsEnabledProperty);
            Assert.Equal(
                "WidgetDisabledBrush",
                ResourceKeyOf(RequireSetter(disabled, "Surface", Panel.BackgroundProperty)));
            Assert.Equal(
                "WidgetBorderBrush",
                ResourceKeyOf(RequireSetter(disabled, "Surface", Border.BorderBrushProperty)));
            Assert.Equal(
                "PanelDisabledForegroundBrush",
                ResourceKeyOf(RequireSetter(disabled, targetName: null, Control.ForegroundProperty)));
            Assert.Same(disabled, template.Triggers.OfType<Trigger>().Last());
        });
    }

    [Theory]
    [InlineData(ThemeIds.Dark)]
    [InlineData(ThemeIds.Light)]
    [InlineData(ThemeIds.HighContrast)]
    [InlineData(ThemeIds.Midnight)]
    [InlineData(ThemeIds.Nord)]
    [InlineData(ThemeIds.Oled)]
    public async Task HoverAndPressedSurfaces_KeepTheThemeForegroundReadable(string themeId)
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var theme = LoadTheme(themeId);
            var foreground = ColorOf(theme, "PanelForegroundBrush");

            // Hover: the theme foreground on WidgetPressedBrush (the light stock system hover is gone).
            var hoverContrast = Contrast(foreground, ColorOf(theme, "WidgetPressedBrush"));
            Assert.True(
                hoverContrast >= 4.5,
                $"Theme '{themeId}' hover contrast is {hoverContrast:0.00}:1, expected at least 4.5:1.");

            // Pressed: the same foreground on the darker disabled surface.
            var pressedContrast = Contrast(foreground, ColorOf(theme, "WidgetDisabledBrush"));
            Assert.True(
                pressedContrast >= 4.5,
                $"Theme '{themeId}' pressed contrast is {pressedContrast:0.00}:1, expected at least 4.5:1.");

            // Disabled: intentionally muted, but still distinguishable from its surface (WCAG exempts
            // disabled controls from the 4.5:1 text requirement).
            var disabledContrast = Contrast(
                ColorOf(theme, "PanelDisabledForegroundBrush"),
                ColorOf(theme, "WidgetDisabledBrush"));
            Assert.True(
                disabledContrast >= 2.5,
                $"Theme '{themeId}' disabled contrast is {disabledContrast:0.00}:1, expected at least 2.5:1.");
        });
    }

    [Fact]
    public async Task AboutButtons_UseTheSharedThemedTemplate_AndALinkHoverForeground()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewModel = new AboutViewModel(new FakeUpdateCheckService(), new RecordingUrlLauncher());
            var window = new AboutWindow(viewModel);
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                var styles = Assert.IsType<ResourceDictionary>(window.Resources.MergedDictionaries[0]);
                var themed = Assert.IsType<Style>(styles["ThemedButtonStyle"]);
                var template = Assert.IsType<ControlTemplate>(TemplateOf(themed));

                var buttonNames = new[]
                {
                    "RepositoryButton",
                    "ReleasesButton",
                    "CheckForUpdatesButton",
                    "OpenReleaseButton",
                    "CloseButton",
                };

                var allThemed = true;
                foreach (var name in buttonNames)
                {
                    var button = Assert.IsType<Button>(window.FindName(name));
                    allThemed &= ReferenceEquals(button.Template, template);
                    allThemed &= BuildsOn(button.Style, themed);
                }

                var link = Assert.IsType<Button>(window.FindName("RepositoryButton")).Style!;
                var linkBackground = link.Setters
                    .OfType<Setter>()
                    .Single(setter => setter.Property == Panel.BackgroundProperty);

                // A hovered link keeps the WidgetPressedBrush surface of the shared template but
                // switches from the accent to the theme foreground, because the accent only reaches
                // ~3:1 on that surface in the dark palettes.
                var linkHover = Assert.Single(
                    link.Triggers.OfType<Trigger>(),
                    trigger => trigger.Property == UIElement.IsMouseOverProperty);

                return new
                {
                    AllThemed = allThemed,
                    LinkBackgroundIsTransparent =
                        Assert.IsType<SolidColorBrush>(linkBackground.Value).Color.A == 0,
                    LinkHoverForeground = ResourceKeyOf(
                        RequireSetter(linkHover, targetName: null, Control.ForegroundProperty)),
                    HasTemplateHover = template.Triggers
                        .OfType<Trigger>()
                        .Any(trigger => trigger.Property == UIElement.IsMouseOverProperty),
                };
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(result.AllThemed);
        Assert.True(result.HasTemplateHover);
        Assert.True(result.LinkBackgroundIsTransparent);
        Assert.Equal("PanelForegroundBrush", result.LinkHoverForeground);
    }

    [Fact]
    public async Task ExistingExplicitButtonStyles_KeepTheirOwnThemedTemplates()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var styles = LoadStyles();

            // Regression guard for the audit: every Button style in the shared dictionary must reach a
            // custom template, so none of them can fall back to the stock light hover chrome.
            foreach (var style in styles.Values.OfType<Style>().Where(s => s.TargetType == typeof(Button)))
            {
                Assert.True(
                    TemplateOf(style) is not null,
                    $"Button style '{style}' does not provide a themed ControlTemplate.");
            }

            // The touch-first widget styles deliberately have no hover state (RF-025: no affordance
            // depends on hover), and they already use the palette brushes.
            foreach (var key in new[] { "TouchButtonStyle", "TouchToggleButtonStyle" })
            {
                var template = Assert.IsType<ControlTemplate>(TemplateOf(Assert.IsType<Style>(styles[key])));
                Assert.DoesNotContain(
                    template!.Triggers.OfType<Trigger>(),
                    trigger => trigger.Property == UIElement.IsMouseOverProperty);
            }
        });
    }

    private static ControlTemplate? TemplateOf(Style style)
    {
        for (var current = style; current is not null; current = current.BasedOn)
        {
            var setter = current.Setters
                .OfType<Setter>()
                .FirstOrDefault(candidate => candidate.Property == Control.TemplateProperty);
            if (setter?.Value is ControlTemplate template)
            {
                return template;
            }
        }

        return null;
    }

    private static bool BuildsOn(Style? style, Style expected)
    {
        for (var current = style; current is not null; current = current.BasedOn)
        {
            if (ReferenceEquals(current, expected))
            {
                return true;
            }
        }

        return false;
    }

    private static Trigger RequireTrigger(ControlTemplate template, DependencyProperty property) =>
        Assert.Single(
            template.Triggers.OfType<Trigger>(),
            trigger => trigger.Property == property);

    private static Setter RequireSetter(Trigger trigger, string? targetName, DependencyProperty property) =>
        Assert.Single(
            trigger.Setters.OfType<Setter>(),
            setter => setter.Property == property
                && string.Equals(setter.TargetName ?? string.Empty, targetName ?? string.Empty, StringComparison.Ordinal));

    private static string ResourceKeyOf(Setter setter) =>
        Assert.IsType<DynamicResourceExtension>(setter.Value).ResourceKey as string
            ?? throw new InvalidOperationException("The setter does not use a DynamicResource palette key.");

    private static Button Realize(Button button, Panel host)
    {
        host.Children.Add(button);
        host.Measure(new Size(320, 120));
        host.Arrange(new Rect(0, 0, 320, 120));
        host.UpdateLayout();
        button.ApplyTemplate();
        return button;
    }

    private static Border RequireTemplateBorder(Button button, string name) =>
        Assert.IsType<Border>(button.Template.FindName(name, button));

    private static double Contrast(Color first, Color second)
    {
        var lighter = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        var darker = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color) =>
        (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

    private static double Channel(byte value)
    {
        var srgb = value / 255.0;
        return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    private static Color BrushColor(Brush? brush) =>
        Assert.IsType<SolidColorBrush>(brush).Color;

    private static Color ColorOf(ResourceDictionary dictionary, string key) =>
        Assert.IsType<SolidColorBrush>(dictionary[key]).Color;

    private static List<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var found = new List<T>();
        Walk(root, found);
        return found;
    }

    private static void Walk<T>(DependencyObject node, List<T> found)
        where T : DependencyObject
    {
        if (node is T match)
        {
            found.Add(match);
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            Walk(VisualTreeHelper.GetChild(node, i), found);
        }
    }

    private static ResourceDictionary LoadStyles() =>
        Load("/TouchPanel;component/Theme/Styles.xaml");

    private static ResourceDictionary LoadTheme(string themeId)
    {
        Assert.True(ThemeIds.TryGetResourceUri(themeId, out var uri));
        return Assert.IsType<ResourceDictionary>(Application.LoadComponent(uri));
    }

    private static ResourceDictionary Load(string componentUri) =>
        Assert.IsType<ResourceDictionary>(
            Application.LoadComponent(new Uri(componentUri, UriKind.Relative)));
}
