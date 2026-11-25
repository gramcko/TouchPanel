using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Theme;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Property-editor label/icon/colors behavior (T-14 part 2, RF-043/M2-RF-435/M2-RF-436): the editor
/// offers the built-in icon catalog, stores a chosen icon id, parses/normalizes color overrides,
/// clamps font size and opacity, blocks invalid input with feedback without mutating the config,
/// updates the live widget without a rebuild, marks the layout dirty, and persists through the
/// existing single-writer save-on-exit path.
/// </summary>
public sealed class PropertyEditorAppearanceTests
{
    private const string PageId = "main";

    [Fact]
    public void Editor_OffersTheBuiltInIconCatalogHeadedByNone()
    {
        var editor = EditorFor(Label("l1"));

        Assert.True(editor.SupportsAppearance);
        Assert.Equal(IconCatalog.BuiltIn.Count + 1, editor.IconOptions.Count);
        Assert.Equal(string.Empty, editor.IconOptions[0].Id);
        Assert.Equal("(none)", editor.IconOptions[0].DisplayName);
        Assert.Equal(string.Empty, editor.SelectedIcon.Id);
    }

    [Fact]
    public void IconEdit_UpdatesConfigLiveWidgetAndDirty()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        var power = IconCatalog.BuiltIn.First(option => option.Id == "power");
        editor.SelectedIcon = power;

        Assert.Equal("power", context.Config.Pages[0].Widgets[0].Icon);
        Assert.Equal(power.Glyph, widget.IconGlyph);
        Assert.True(widget.HasIcon);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void IconEdit_NoneClearsTheIcon()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.SelectedIcon = IconCatalog.BuiltIn.First(option => option.Id == "power");
        editor.SelectedIcon = editor.IconOptions[0];

        Assert.Null(context.Config.Pages[0].Widgets[0].Icon);
        Assert.Null(widget.IconGlyph);
        Assert.False(widget.HasIcon);
    }

    [Fact]
    public void ForegroundEdit_ValidNormalizesAndUpdatesLive()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.ForegroundText = "#ff8800";

        Assert.False(editor.HasValidationError);
        Assert.Equal("#FF8800", context.Config.Pages[0].Widgets[0].Foreground);
        Assert.Equal("#FF8800", editor.ForegroundText);
        Assert.NotNull(widget.ForegroundBrush);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void BackgroundAndAccentEdit_UpdateConfigAndLive()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.BackgroundText = "#80112233";
        editor.AccentText = "#AABBCC";

        Assert.Equal("#80112233", context.Config.Pages[0].Widgets[0].Background);
        Assert.Equal("#AABBCC", context.Config.Pages[0].Widgets[0].Accent);
        Assert.NotNull(widget.BackgroundBrush);
        Assert.NotNull(widget.AccentBrush);
    }

    [Fact]
    public void ColorEdit_InvalidIsBlockedWithFeedbackAndNoMutation()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.ForegroundText = "not-a-color";

        Assert.True(editor.HasValidationError);
        Assert.Null(context.Config.Pages[0].Widgets[0].Foreground);
        Assert.Null(widget.ForegroundBrush);
    }

    [Fact]
    public void ColorEdit_EmptyClearsTheOverride()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.ForegroundText = "#112233";
        Assert.NotNull(widget.ForegroundBrush);

        editor.ForegroundText = string.Empty;

        Assert.Null(context.Config.Pages[0].Widgets[0].Foreground);
        Assert.Null(widget.ForegroundBrush);
    }

    [Fact]
    public void ColorEdit_RaisesTheInheritanceCompanionForThePickerHeader()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var changes = new List<string>();
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is { } name)
            {
                changes.Add(name);
            }
        };

        Assert.True(editor.IsForegroundInherited);

        editor.ForegroundText = "#112233";

        Assert.False(editor.IsForegroundInherited);
        Assert.Contains(nameof(PropertyEditorViewModel.IsForegroundInherited), changes);

        editor.ForegroundText = string.Empty;

        Assert.True(editor.IsForegroundInherited);
        Assert.Contains(nameof(PropertyEditorViewModel.IsForegroundInherited), changes);
    }

    [Fact]
    public void ColorEdit_IsOneUndoStepPerAppliedSelection()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;

        Assert.Equal(0, context.Service.UndoDepth);

        editor.ForegroundText = "#112233";
        Assert.Equal(1, context.Service.UndoDepth);

        editor.ForegroundText = "#445566";
        Assert.Equal(2, context.Service.UndoDepth);

        // Re-applying the same value is a no-op (no history noise).
        editor.ForegroundText = "#445566";
        Assert.Equal(2, context.Service.UndoDepth);

        Assert.True(context.Service.TryUndo());
        Assert.Equal("#112233", context.Config.Pages[0].Widgets[0].Foreground);

        Assert.True(context.Service.TryUndo());
        Assert.Null(context.Config.Pages[0].Widgets[0].Foreground);
    }

    [Fact]
    public void OnIconColor_IsOfferedOnlyForTogglesAndStartsInherited()
    {
        var toggle = EditorFor(Toggle("t1"));
        var label = EditorFor(Label("l1"));

        Assert.True(toggle.SupportsOnIconColor);
        Assert.False(label.SupportsOnIconColor);
        Assert.True(toggle.IsOnIconColorInherited);
        Assert.Equal(string.Empty, toggle.OnIconColorText);
    }

    [Fact]
    public void OnIconColorEdit_AppliesLiveAndClearingRestoresTheContrastingDefault()
    {
        var context = CreateContext(Toggle("t1", initialOn: true));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = Assert.IsType<ToggleWidgetViewModel>(context.Widgets[0]);

        Assert.Null(widget.OnIconBrush);
        Assert.Null(widget.DisplayIconBrush);

        editor.OnIconColorText = "#ff8800";

        Assert.False(editor.HasValidationError);
        Assert.False(editor.IsOnIconColorInherited);
        Assert.Equal("#FF8800", ((ToggleWidgetConfig)editor.Config).OnIconColor);
        Assert.Equal(Color.FromRgb(0xFF, 0x88, 0x00), (widget.OnIconBrush as SolidColorBrush)?.Color);
        Assert.Same(widget.OnIconBrush, widget.DisplayIconBrush);
        Assert.True(context.Service.IsDirty);
        Assert.Equal(1, context.Service.UndoDepth);

        editor.OnIconColorText = string.Empty;

        Assert.Null(((ToggleWidgetConfig)editor.Config).OnIconColor);
        Assert.Null(widget.OnIconBrush);
        Assert.Null(widget.DisplayIconBrush);
        Assert.True(editor.IsOnIconColorInherited);
        Assert.Equal(2, context.Service.UndoDepth);
    }

    [Fact]
    public void OnIconColorEdit_InvalidIsBlockedWithFeedbackAndNoMutation()
    {
        var context = CreateContext(Toggle("t1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.OnIconColorText = "not-a-color";

        Assert.True(editor.HasValidationError);
        Assert.Null(((ToggleWidgetConfig)editor.Config).OnIconColor);
        Assert.True(editor.IsOnIconColorInherited);
    }

    [Fact]
    public void FontSizeEdit_ClampsToTheConfiguredBounds()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.FontSizeText = "200";
        Assert.Equal(WidgetAppearance.MaxFontSize, context.Config.Pages[0].Widgets[0].FontSize);
        Assert.Equal(WidgetAppearance.MaxFontSize, widget.FontSize);
        Assert.Equal("96", editor.FontSizeText);

        editor.FontSizeText = "2";
        Assert.Equal(WidgetAppearance.MinFontSize, widget.FontSize);

        editor.FontSizeText = string.Empty;
        Assert.Null(context.Config.Pages[0].Widgets[0].FontSize);
        Assert.Null(widget.FontSize);
    }

    [Fact]
    public void FontSizeEdit_NonNumericIsBlockedWithoutMutation()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.FontSizeText = "abc";

        Assert.True(editor.HasValidationError);
        Assert.Null(context.Config.Pages[0].Widgets[0].FontSize);
    }

    [Fact]
    public void OpacityEdit_ClampsToTheConfiguredBounds()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.OpacityText = "2";
        Assert.Equal(WidgetAppearance.MaxOpacity, context.Config.Pages[0].Widgets[0].Opacity);
        Assert.Equal(WidgetAppearance.MaxOpacity, widget.Opacity);

        editor.OpacityText = "-1";
        Assert.Equal(WidgetAppearance.MinOpacity, widget.Opacity);

        editor.OpacityText = "abc";
        Assert.True(editor.HasValidationError);
        Assert.Equal(WidgetAppearance.MinOpacity, widget.Opacity);
    }

    [Fact]
    public async Task AppearanceEdits_PersistThroughTheSingleWriterOnExit()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.SelectedIcon = IconCatalog.BuiltIn.First(option => option.Id == "star");
        editor.ForegroundText = "#112233";
        editor.BackgroundText = "#80112233";
        editor.AccentText = "#AABBCC";
        editor.FontSizeText = "24";
        editor.OpacityText = "0.6";

        var result = await context.Service.ExitAsync();

        Assert.True(result.Succeeded);
        var saved = Assert.IsType<LabelWidgetConfig>(context.Store.LastSavedConfig!.Pages[0].Widgets[0]);
        Assert.Equal("star", saved.Icon);
        Assert.Equal("#112233", saved.Foreground);
        Assert.Equal("#80112233", saved.Background);
        Assert.Equal("#AABBCC", saved.Accent);
        Assert.Equal(24, saved.FontSize);
        Assert.Equal(0.6, saved.Opacity);
    }

    [Fact]
    public void AppearanceFields_ShowThemeDefaultsForNullOverrides()
    {
        // M5-RF-542: a widget with no appearance override shows the active theme's default rather
        // than a blank or a hard-coded constant.
        var editor = EditorFor(Label("l1"));
        var defaults = ThemeAppearanceDefaults.For(ThemeIds.Dark);

        Assert.Equal(defaults.Foreground, editor.ForegroundText);
        Assert.Equal(defaults.Background, editor.BackgroundText);
        Assert.Equal(defaults.Accent, editor.AccentText);
        Assert.Equal(defaults.FontSize.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), editor.FontSizeText);
        Assert.Equal(defaults.Opacity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), editor.OpacityText);
        Assert.Equal(defaults, editor.ThemeDefaults);
    }

    [Fact]
    public void FocusBlurWithoutEditing_DoesNotCreateAnOverride()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        // Simulate the WPF LostFocus two-way binding writing the displayed (theme-default) text back.
        editor.ForegroundText = editor.ForegroundText;
        editor.BackgroundText = editor.BackgroundText;
        editor.AccentText = editor.AccentText;
        editor.FontSizeText = editor.FontSizeText;
        editor.OpacityText = editor.OpacityText;

        var cfg = context.Config.Pages[0].Widgets[0];
        Assert.Null(cfg.Foreground);
        Assert.Null(cfg.Background);
        Assert.Null(cfg.Accent);
        Assert.Null(cfg.FontSize);
        Assert.Null(cfg.Opacity);

        // The live widget stays inherited (null brushes/values), so the dynamic theme fallback still
        // applies; the editor did not mark the layout dirty either.
        Assert.Null(widget.ForegroundBrush);
        Assert.Null(widget.BackgroundBrush);
        Assert.Null(widget.AccentBrush);
        Assert.Null(widget.FontSize);
        Assert.Null(widget.Opacity);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void RestoringTheThemeDefaultTextWhileInherited_DoesNotPinAnyField()
    {
        // M5-RF-543: while a field is inherited, entering the exact theme-default text must leave it
        // inherited rather than pin it, so a later theme switch can still restyle it.
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var defaults = ThemeAppearanceDefaults.For(ThemeIds.Dark);
        var cfg = context.Config.Pages[0].Widgets[0];

        // Put each field into a rejected, uncommitted state (no override), then restore the displayed
        // theme default; the field text differs from the default, so the guard is what keeps it
        // inherited.
        editor.ForegroundText = "not-a-color";
        editor.BackgroundText = "not-a-color";
        editor.AccentText = "not-a-color";
        editor.FontSizeText = "abc";
        editor.OpacityText = "abc";
        Assert.True(editor.HasValidationError);

        editor.ForegroundText = defaults.Foreground;
        editor.BackgroundText = defaults.Background;
        editor.AccentText = defaults.Accent;
        editor.FontSizeText = editor.ThemeFontSizeText;
        editor.OpacityText = editor.ThemeOpacityText;

        Assert.False(editor.HasValidationError);
        Assert.Null(cfg.Foreground);
        Assert.Null(cfg.Background);
        Assert.Null(cfg.Accent);
        Assert.Null(cfg.FontSize);
        Assert.Null(cfg.Opacity);
    }

    [Fact]
    public void ExplicitEdit_CreatesAnOverride_AndClearingRestoresInheritance()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var cfg = context.Config.Pages[0].Widgets[0];

        editor.ForegroundText = "#112233";
        Assert.Equal("#112233", cfg.Foreground);

        editor.ForegroundText = string.Empty;
        Assert.Null(cfg.Foreground);
        // Clearing shows the inherited theme default again rather than leaving a blank field.
        Assert.Equal(ThemeAppearanceDefaults.For(ThemeIds.Dark).Foreground, editor.ForegroundText);
    }

    [Fact]
    public async Task FocusBlurThenLiveThemeSwap_StillRestylesTheInheritedField()
    {
        // M5-RF-543: after a focus+blur that writes the theme default back, the field must remain
        // inherited so a live theme switch still restyles it. Realizes the Label against a themed
        // palette, swaps the palette in place (a ThemeManager stand-in), and asserts the rendered
        // foreground follows.
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var context = CreateContext(Label("l1"));
            context.EnterSelect();
            var editor = context.Editor;

            editor.ForegroundText = editor.ForegroundText;
            Assert.Null(context.Config.Pages[0].Widgets[0].Foreground);

            var palette = (ResourceDictionary)Application.LoadComponent(
                new Uri("/TouchPanel;component/Theme/Colors.xaml", UriKind.Relative));
            var templates = (ResourceDictionary)Application.LoadComponent(
                new Uri("/TouchPanel;component/Widgets/LabelWidget/LabelWidgetTemplates.xaml", UriKind.Relative));
            var template = (DataTemplate)templates["Widget.Label"];

            var host = new Grid();
            host.Resources.MergedDictionaries.Add(palette);
            var presenter = new ContentPresenter { Content = context.Widgets[0], ContentTemplate = template };
            host.Children.Add(presenter);
            host.Measure(new Size(200, 100));
            host.Arrange(new Rect(0, 0, 200, 100));
            host.UpdateLayout();

            var label = FindDescendant<TextBlock>(presenter, block => block.Tag is SolidColorBrush);
            Assert.NotNull(label);
            var themedColor = Assert.IsType<SolidColorBrush>(label!.Foreground).Color;

            // Swap the theme brush in place; the inherited field re-resolves to the new theme color.
            var restyled = Color.FromRgb(0x11, 0x22, 0x33);
            palette["PanelForegroundBrush"] = new SolidColorBrush(restyled);
            host.UpdateLayout();

            Assert.NotEqual(themedColor, restyled);
            Assert.Equal(restyled, Assert.IsType<SolidColorBrush>(label.Foreground).Color);
        });
    }

    [Fact]
    public void ExplicitOverride_WinsOverTheThemeDefault()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.ForegroundText = "#112233";

        Assert.Equal("#112233", editor.ForegroundText);
        Assert.NotEqual(ThemeAppearanceDefaults.For(ThemeIds.Dark).Foreground, editor.ForegroundText);
    }

    [Fact]
    public void FontSizeLiveEdit_PreviewsTheClampedValueWithoutRewritingTheTypedText()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.BeginLiveEdit();
        editor.FontSizeText = "1";
        Assert.Equal(WidgetAppearance.MinFontSize, widget.FontSize);
        Assert.Equal("1", editor.FontSizeText);

        editor.FontSizeText = "15";
        Assert.Equal(15, widget.FontSize);
        Assert.Equal("15", editor.FontSizeText);

        editor.FontSizeText = "120";
        Assert.Equal(WidgetAppearance.MaxFontSize, widget.FontSize);
        Assert.Equal("120", editor.FontSizeText);

        editor.CommitLiveEdit();
        Assert.Equal(WidgetAppearance.MaxFontSize, widget.FontSize);
        Assert.Equal(WidgetAppearance.MaxFontSize, context.Config.Pages[0].Widgets[0].FontSize);
        Assert.Equal("96", editor.FontSizeText);
        Assert.Equal(1, context.Service.UndoDepth);
    }

    [Fact]
    public void FontSizeLiveEdit_InvalidIntermediateInputShowsTheErrorAndLeavesTheWidgetUnchanged()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.BeginLiveEdit();
        editor.FontSizeText = "30";
        Assert.Equal(30, widget.FontSize);

        editor.FontSizeText = "abc";
        Assert.True(editor.HasValidationError);
        Assert.Equal("abc", editor.FontSizeText);
        Assert.Equal(30, widget.FontSize);
        Assert.Equal(30, context.Config.Pages[0].Widgets[0].FontSize);

        editor.FontSizeText = "42";
        Assert.False(editor.HasValidationError);
        Assert.Equal(42, widget.FontSize);
    }

    [Fact]
    public void FontSizeLiveEdit_EmptyIntermediateInputDoesNotClearTheOverride()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.BeginLiveEdit();
        editor.FontSizeText = string.Empty;

        Assert.Equal(string.Empty, editor.FontSizeText);
        Assert.Equal(24, widget.FontSize);
        Assert.Equal(24, context.Config.Pages[0].Widgets[0].FontSize);

        editor.FontSizeText = "18";
        Assert.False(editor.HasValidationError);
        Assert.Equal(18, widget.FontSize);
    }

    [Fact]
    public void FontSizeLiveEdit_EmptyCommitClearsTheOverrideAndRestoresTheThemeDefaultText()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.BeginLiveEdit();
        editor.FontSizeText = string.Empty;
        editor.CommitLiveEdit();

        Assert.Null(context.Config.Pages[0].Widgets[0].FontSize);
        Assert.Null(widget.FontSize);
        Assert.Equal(editor.ThemeFontSizeText, editor.FontSizeText);
        Assert.False(editor.HasValidationError);
    }

    [Fact]
    public void FontSizeLiveEdit_FocusBlurOrClearWithoutEditingKeepsTheThemeDefaultInherited()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        // A pure focus+blur must not pin the displayed theme default as an override.
        editor.BeginLiveEdit();
        editor.CommitLiveEdit();
        Assert.Null(context.Config.Pages[0].Widgets[0].FontSize);
        Assert.Null(widget.FontSize);
        Assert.Equal(editor.ThemeFontSizeText, editor.FontSizeText);
        Assert.False(context.Service.IsDirty);

        // Deleting an inherited field and leaving it also keeps inheritance and stays clean.
        editor.BeginLiveEdit();
        editor.FontSizeText = string.Empty;
        editor.CommitLiveEdit();
        Assert.Null(context.Config.Pages[0].Widgets[0].FontSize);
        Assert.Null(widget.FontSize);
        Assert.Equal(editor.ThemeFontSizeText, editor.FontSizeText);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void FontSizeLiveEdit_InvalidCommitRestoresTheLastValidTextAndKeepsTheWidgetValid()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        // No valid change yet: the text from before typing is the fallback.
        editor.BeginLiveEdit();
        editor.FontSizeText = "abc";
        editor.CommitLiveEdit();
        Assert.Equal("24", editor.FontSizeText);
        Assert.True(editor.HasValidationError);
        Assert.Equal(24, widget.FontSize);

        // A valid preview followed by an invalid commit restores the last valid raw text.
        editor.BeginLiveEdit();
        editor.FontSizeText = "30";
        editor.FontSizeText = "abc";
        editor.CommitLiveEdit();
        Assert.Equal("30", editor.FontSizeText);
        Assert.True(editor.HasValidationError);
        Assert.Equal(30, widget.FontSize);
        Assert.Equal(30, context.Config.Pages[0].Widgets[0].FontSize);
    }

    [Fact]
    public void FontSizeLiveEdit_IsOneUndoGroupAndTheNextSessionStartsANewGroup()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        Assert.Equal(0, context.Service.UndoDepth);

        editor.BeginLiveEdit();
        editor.FontSizeText = "1";
        Assert.Equal(WidgetAppearance.MinFontSize, context.Widgets[0].FontSize);
        editor.FontSizeText = "15";
        Assert.Equal(15, context.Widgets[0].FontSize);
        Assert.Equal(1, context.Service.UndoDepth);

        editor.CommitLiveEdit();
        Assert.Equal("15", editor.FontSizeText);
        Assert.Equal(1, context.Service.UndoDepth);

        editor.BeginLiveEdit();
        editor.FontSizeText = "30";
        Assert.Equal(30, context.Widgets[0].FontSize);
        Assert.Equal(2, context.Service.UndoDepth);
        editor.CommitLiveEdit();

        Assert.True(context.Service.TryUndo());
        Assert.Equal(15, context.Service.Widgets[0].FontSize);
        Assert.Equal(15, context.Config.Pages[0].Widgets[0].FontSize);
        Assert.Equal(1, context.Service.UndoDepth);

        Assert.True(context.Service.TryUndo());
        Assert.Equal(24, context.Service.Widgets[0].FontSize);
        Assert.Equal(24, context.Config.Pages[0].Widgets[0].FontSize);
        Assert.Equal(0, context.Service.UndoDepth);
    }

    [Fact]
    public void EditOverlay_AppearanceNumericFieldsShareTheLiveNumericInputBehavior()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "TouchPanel", "Views", "EditOverlay.xaml"));

        var fontSize = ExtractElement(xaml, "FontSizeBox");
        Assert.Contains("UpdateSourceTrigger=PropertyChanged", fontSize, StringComparison.Ordinal);
        Assert.Contains("NumericInput.Field=\"FontSizeText\"", fontSize, StringComparison.Ordinal);

        // Opacity adopted the same live-preview lifecycle as the Font size field.
        var opacity = ExtractElement(xaml, "OpacityBox");
        Assert.Contains("UpdateSourceTrigger=PropertyChanged", opacity, StringComparison.Ordinal);
        Assert.Contains("NumericInput.Field=\"OpacityText\"", opacity, StringComparison.Ordinal);
    }

    private static string ExtractElement(string xaml, string name)
    {
        var start = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The {name} element must exist.");
        var end = xaml.IndexOf("/>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"The {name} element must be self-closing.");
        return xaml[start..end];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TouchPanel.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        if (root is T match && predicate(match))
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindDescendant(VisualTreeHelper.GetChild(root, i), predicate) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static PropertyEditorViewModel EditorFor(WidgetConfig widget)
    {
        var context = CreateContext(widget);
        context.EnterSelect();
        return context.Editor;
    }

    private static TestContext CreateContext(params WidgetConfig[] widgets)
    {
        var config = ConfigWith(widgets);
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var store = new RecordingConfigStore();
        var service = new EditModeService(config, page, store, factory: registry, palette: registry);
        return new TestContext(config, page, store, service);
    }

    private static PanelConfig ConfigWith(params WidgetConfig[] widgets) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = PageId,
        Pages = [new PageConfig { Id = PageId, Widgets = [.. widgets] }],
    };

    private static ToggleWidgetConfig Toggle(string id, bool initialOn = false) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 200,
        Height = 100,
        Label = id,
        Icon = "power",
        OnIcon = "star",
        InitialOn = initialOn,
        Action = new MuteActionConfig(),
    };

    private static LabelWidgetConfig Label(string id, double? fontSize = null) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 200,
        Height = 100,
        Label = id,
        FontSize = fontSize,
    };

    private sealed record TestContext(
        PanelConfig Config,
        PageViewModel Page,
        RecordingConfigStore Store,
        EditModeService Service)
    {
        public IReadOnlyList<WidgetViewModelBase> Widgets => Page.Widgets;

        public PropertyEditorViewModel Editor => Service.PropertyEditor!;

        public void EnterSelect()
        {
            Service.Enter(1000, 500);
            Service.SelectWidget(Widgets[0]);
            Service.OpenPropertyEditor();
        }
    }
}
