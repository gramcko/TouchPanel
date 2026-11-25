using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.ViewModels.Settings;
using TouchPanel.Views;
using TouchPanel.Views.Controls;
using TouchPanel.Views.Settings;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// User-requested inline colour picker: the shared palette (24 swatches, six per row), the collapsed
/// swatch/hex header, inline expansion, exact-hex parsing (#RGB/#RRGGBB/#AARRGGBB), "None" clearing,
/// live+undoable application, 40 DIP touch targets, panel fit, and the BAML wiring of all five
/// appearance colour fields (widget foreground/background/accent/on-icon and the page background).
/// </summary>
public sealed class InlineColorPickerTests
{
    [Fact]
    public void Palette_HasTwentyFourDistinctSwatchesInSixColumnOrder()
    {
        Assert.Equal(24, ColorPalette.Standard.Count);
        Assert.Equal(0, ColorPalette.Standard.Count % ColorPalette.Columns);
        Assert.Equal(6, ColorPalette.Columns);
        Assert.All(ColorPalette.Standard, swatch =>
        {
            Assert.True(WidgetAppearance.TryNormalizeColor(swatch.Hex, out var normalized));
            Assert.Equal(swatch.Hex, normalized);
            Assert.False(string.IsNullOrWhiteSpace(swatch.Name));
        });

        Assert.Equal(
            ColorPalette.Standard.Count,
            ColorPalette.Standard.Select(swatch => swatch.Hex).Distinct(StringComparer.Ordinal).Count());

        // Neutrals/theme darks first, then the accent blue and vivid hues.
        Assert.Equal("#000000", ColorPalette.Standard[0].Hex);
        Assert.Equal("#FFFFFF", ColorPalette.Standard[1].Hex);
        Assert.Contains(ColorPalette.Standard, swatch => swatch.Hex == "#0E1620");
        Assert.Contains(ColorPalette.Standard, swatch => swatch.Hex == "#3D7EFF");
        Assert.Contains(ColorPalette.Standard, swatch => swatch.Name == "Red");
        Assert.Contains(ColorPalette.Standard, swatch => swatch.Name == "Pink");
    }

    [Theory]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("#4080FF", "#4080FF")]
    [InlineData("#804080FF", "#804080FF")]
    [InlineData("  #12aBcD  ", "#12ABCD")]
    public void HexParsing_AcceptsThreeSixAndEightDigits(string input, string expected)
    {
        Assert.True(ColorPalette.TryParseHex(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("red")]
    [InlineData("#")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("123456")]
    [InlineData("#GG0000")]
    public void HexParsing_RejectsMalformedValues(string? input)
    {
        Assert.False(ColorPalette.TryParseHex(input, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public async Task Picker_StartsCollapsedAndShowsTheCurrentSwatchAndValue()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var picker = new InlineColorPicker { SelectedHex = "#112233" };
            var host = Host(picker, 270, 600);

            var header = Assert.IsType<Button>(picker.FindName("PickerHeader"));
            var headerLabel = Assert.IsType<TextBlock>(picker.FindName("HeaderLabel"));
            var swatch = Assert.IsType<Border>(picker.FindName("CurrentSwatch"));
            var body = Assert.IsType<StackPanel>(picker.FindName("PickerBody"));

            return new
            {
                Expanded = picker.IsExpanded,
                HeaderHeight = header.MinHeight,
                Label = headerLabel.Text,
                SwatchColor = (swatch.Background as SolidColorBrush)?.Color,
                BodyVisibility = body.Visibility,
                SwatchCount = picker.Swatches.Count,
                TileCount = ((System.Collections.IEnumerable)Assert.IsType<ItemsControl>(
                    picker.FindName("SwatchTiles")).ItemsSource!).Cast<object>().Count(),
                Fits = picker.DesiredSize.Width <= 270,
            };
        });

        Assert.False(result.Expanded);
        Assert.True(result.HeaderHeight >= 40);
        Assert.Equal("#112233", result.Label);
        Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), result.SwatchColor);
        Assert.Equal(Visibility.Collapsed, result.BodyVisibility);
        Assert.Equal(24, result.SwatchCount);
        Assert.Equal(24, result.TileCount);
        Assert.True(result.Fits, $"The picker must fit the 300 DIP panel: {result.Fits}.");
    }

    [Fact]
    public async Task Picker_ExpandsToTheSwatchGridAndAnOptionalNoneTile()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var plain = new InlineColorPicker { IsExpanded = true };
            var host = Host(plain, 270, 600);
            var tiles = Assert.IsType<ItemsControl>(plain.FindName("SwatchTiles"));
            var countWithoutNone = tiles.ItemsSource.Cast<ColorSwatchOption>().Count();

            var optional = new InlineColorPicker { IsExpanded = true, AllowNone = true };
            var optionalHost = Host(optional, 270, 600);
            var optionalTiles = Assert.IsType<ItemsControl>(optional.FindName("SwatchTiles"));
            var options = optionalTiles.ItemsSource.Cast<ColorSwatchOption>().ToArray();
            optionalHost.UpdateLayout();

            var panel = Assert.IsType<UniformGrid>(optionalTiles.ItemsPanel.LoadContent());
            var buttons = FindVisualDescendants<Button>(optionalTiles).ToArray();

            return new
            {
                CountWithoutNone = countWithoutNone,
                WithNone = options.Length,
                LastIsNone = options[^1].IsNone,
                LastText = options[^1].DisplayText,
                Columns = panel.Columns,
                MinHeight = buttons.Min(button => button.MinHeight),
                MinWidth = buttons.Min(button => button.MinWidth),
                BodyVisibility = Assert.IsType<StackPanel>(optional.FindName("PickerBody")).Visibility,
            };
        });

        Assert.Equal(24, result.CountWithoutNone);
        Assert.Equal(25, result.WithNone);
        Assert.True(result.LastIsNone);
        Assert.Equal("None", result.LastText);
        Assert.Equal(6, result.Columns);
        Assert.True(result.MinHeight >= 40, $"Swatches must be at least 40 DIP tall; were {result.MinHeight}.");
        Assert.True(result.MinWidth >= 40, $"Swatches must be at least 40 DIP wide; were {result.MinWidth}.");
        Assert.Equal(Visibility.Visible, result.BodyVisibility);
    }

    [Fact]
    public async Task Picker_SelectingASwatchAppliesTheExactHexAndCollapses()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var picker = new InlineColorPicker { IsExpanded = true };
            var host = Host(picker, 270, 600);
            var tiles = Assert.IsType<ItemsControl>(picker.FindName("SwatchTiles"));

            var red = FindVisualDescendants<Button>(tiles)
                .Single(button => (button.DataContext as ColorSwatchOption)?.Hex == "#FF4D4D");
            red.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return new
            {
                picker.SelectedHex,
                picker.IsExpanded,
                Header = Assert.IsType<TextBlock>(picker.FindName("HeaderLabel")).Text,
                Inherited = picker.IsInherited,
            };
        });

        Assert.Equal("#FF4D4D", result.SelectedHex);
        Assert.False(result.IsExpanded);
        Assert.Equal("#FF4D4D", result.Header);
        Assert.False(result.Inherited);
    }

    [Fact]
    public async Task Picker_NoneClearsWhereAllowed()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var picker = new InlineColorPicker
            {
                SelectedHex = "#112233",
                AllowNone = true,
                IsExpanded = true,
            };
            var host = Host(picker, 270, 600);
            var tiles = Assert.IsType<ItemsControl>(picker.FindName("SwatchTiles"));

            var none = FindVisualDescendants<Button>(tiles)
                .Single(button => (button.DataContext as ColorSwatchOption)?.IsNone == true);
            none.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return new
            {
                picker.SelectedHex,
                picker.IsInherited,
                picker.IsExpanded,
                Header = Assert.IsType<TextBlock>(picker.FindName("HeaderLabel")).Text,
            };
        });

        Assert.Equal(string.Empty, result.SelectedHex);
        Assert.True(result.IsInherited);
        Assert.False(result.IsExpanded);
        Assert.Equal("Inherited", result.Header);
    }

    [Theory]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("#123456", "#123456")]
    [InlineData("#80123456", "#80123456")]
    public async Task Picker_HexInputCommitsThreeSixAndEightDigits(string input, string expected)
    {
        using var sta = new StaThread();

        var selected = await sta.InvokeAsync(() =>
        {
            var picker = new InlineColorPicker { IsExpanded = true };
            Host(picker, 270, 600);
            var hex = Assert.IsType<TextBox>(picker.FindName("HexBox"));
            hex.Text = input;

            Assert.True(picker.CommitHex());
            return picker.SelectedHex;
        });

        Assert.Equal(expected, selected);
    }

    [Fact]
    public async Task Picker_InvalidHexIsRejectedWithFeedback()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var picker = new InlineColorPicker { SelectedHex = "#112233", IsExpanded = true };
            Host(picker, 270, 600);
            var hex = Assert.IsType<TextBox>(picker.FindName("HexBox"));
            var error = Assert.IsType<TextBlock>(picker.FindName("HexErrorText"));
            hex.Text = "not-a-colour";

            var committed = picker.CommitHex();

            return new
            {
                Committed = committed,
                Selected = picker.SelectedHex,
                ErrorText = error.Text,
                ErrorVisibility = error.Visibility,
            };
        });

        Assert.False(result.Committed);
        Assert.Equal("#112233", result.Selected);
        Assert.Equal(InlineColorPicker.InvalidColorMessage, result.ErrorText);
        Assert.Equal(Visibility.Visible, result.ErrorVisibility);
    }

    [Fact]
    public void HexInputFilter_AllowsOnlyAFullWidthHexPrefix()
    {
        Assert.True(ColorPalette.IsAllowedHexInput(string.Empty));
        Assert.True(ColorPalette.IsAllowedHexInput("#"));
        Assert.True(ColorPalette.IsAllowedHexInput("#a"));
        Assert.True(ColorPalette.IsAllowedHexInput("#AABBCC"));
        Assert.True(ColorPalette.IsAllowedHexInput("#80112233"));
        Assert.False(ColorPalette.IsAllowedHexInput("AABBCC"));
        Assert.False(ColorPalette.IsAllowedHexInput("#GG"));
        Assert.False(ColorPalette.IsAllowedHexInput("#112233445"));
        Assert.False(ColorPalette.IsAllowedHexInput("##112233"));
    }

    [Fact]
    public async Task WidgetPicker_AppliesLiveAndEachSelectionIsOneUndoStep()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Label());
            var editor = context.Editor;
            var widget = context.Widget;
            var picker = Assert.IsType<InlineColorPicker>(context.Content.FindName("ForegroundPicker"));

            Assert.Equal(editor.ForegroundText, picker.SelectedHex);
            Assert.True(picker.IsInherited);

            var first = SelectSwatch(context.Host, picker, "#FF4D4D");
            var afterFirst = new
            {
                Config = ((LabelWidgetConfig)editor.Config).Foreground,
                WidgetBrush = (widget.ForegroundBrush as SolidColorBrush)?.Color,
                Undo = context.Service.UndoDepth,
                Header = Assert.IsType<TextBlock>(picker.FindName("HeaderLabel")).Text,
                Inherited = picker.IsInherited,
            };

            var second = SelectSwatch(context.Host, picker, "#14B8A6");
            var afterSecond = new
            {
                Config = ((LabelWidgetConfig)editor.Config).Foreground,
                WidgetBrush = (widget.ForegroundBrush as SolidColorBrush)?.Color,
                Undo = context.Service.UndoDepth,
            };

            return new { First = first, Second = second, AfterFirst = afterFirst, AfterSecond = afterSecond };
        });

        Assert.True(result.First);
        Assert.Equal("#FF4D4D", result.AfterFirst.Config);
        Assert.Equal(Color.FromRgb(0xFF, 0x4D, 0x4D), result.AfterFirst.WidgetBrush);
        Assert.Equal(1, result.AfterFirst.Undo);
        Assert.Equal("#FF4D4D", result.AfterFirst.Header);
        Assert.False(result.AfterFirst.Inherited);

        Assert.True(result.Second);
        Assert.Equal("#14B8A6", result.AfterSecond.Config);
        Assert.Equal(Color.FromRgb(0x14, 0xB8, 0xA6), result.AfterSecond.WidgetBrush);
        Assert.Equal(2, result.AfterSecond.Undo);
    }

    [Fact]
    public async Task WidgetPicker_FollowsExternalEditorUpdatesAfterASelection()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Label());
            var editor = context.Editor;
            var picker = Assert.IsType<InlineColorPicker>(context.Content.FindName("ForegroundPicker"));

            SelectSwatch(context.Host, picker, "#FF4D4D");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var applied = picker.SelectedHex;

            editor.ForegroundText = "#000000";
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return new { Applied = applied, Updated = picker.SelectedHex, Inherited = picker.IsInherited };
        });

        Assert.Equal("#FF4D4D", result.Applied);
        Assert.Equal("#000000", result.Updated);
        Assert.False(result.Inherited);
    }

    [Fact]
    public async Task WidgetPicker_NoneClearsTheOverride()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Label());
            var editor = context.Editor;
            var widget = context.Widget;
            var picker = Assert.IsType<InlineColorPicker>(context.Content.FindName("ForegroundPicker"));

            SelectSwatch(context.Host, picker, "#FF4D4D");
            var applied = ((LabelWidgetConfig)editor.Config).Foreground;

            var tiles = Assert.IsType<ItemsControl>(picker.FindName("SwatchTiles"));
            Assert.IsType<Button>(picker.FindName("PickerHeader"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            context.Host.UpdateLayout();

            var none = FindVisualDescendants<Button>(tiles)
                .Single(button => (button.DataContext as ColorSwatchOption)?.IsNone == true);
            none.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return new
            {
                Applied = applied,
                Cleared = ((LabelWidgetConfig)editor.Config).Foreground,
                WidgetBrush = widget.ForegroundBrush,
                Inherited = editor.IsForegroundInherited,
                picker.IsExpanded,
            };
        });

        Assert.Equal("#FF4D4D", result.Applied);
        Assert.Null(result.Cleared);
        Assert.Null(result.WidgetBrush);
        Assert.True(result.Inherited);
        Assert.False(result.IsExpanded);
    }

    [Fact]
    public async Task ToggleEditor_ShowsTheOnIconColourPickerAndAppliesItLive()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Toggle());
            var editor = context.Editor;
            var widget = Assert.IsType<ToggleWidgetViewModel>(context.Widget);
            var section = Assert.IsType<StackPanel>(context.Content.FindName("OnIconSection"));
            var picker = Assert.IsType<InlineColorPicker>(context.Content.FindName("OnIconColorPicker"));

            Assert.True(picker.AllowNone);
            Assert.True(picker.IsInherited);

            SelectSwatch(context.Host, picker, "#FFB000");

            return new
            {
                SectionVisibility = section.Visibility,
                Supports = editor.SupportsOnIconColor,
                Config = ((ToggleWidgetConfig)editor.Config).OnIconColor,
                WidgetColor = (widget.OnIconBrush as SolidColorBrush)?.Color,
                widget.DisplayIconBrush,
                Undo = context.Service.UndoDepth,
                Inherited = editor.IsOnIconColorInherited,
            };
        });

        Assert.Equal(Visibility.Visible, result.SectionVisibility);
        Assert.True(result.Supports);
        Assert.Equal("#FFB000", result.Config);
        Assert.Equal(Color.FromRgb(0xFF, 0xB0, 0x00), result.WidgetColor);
        Assert.Equal(result.WidgetColor, (result.DisplayIconBrush as SolidColorBrush)?.Color);
        Assert.Equal(1, result.Undo);
        Assert.False(result.Inherited);
    }

    [Fact]
    public async Task PagesEditor_PickerAppliesThePageBackground()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var config = new PanelConfig
            {
                SchemaVersion = PanelConfig.CurrentSchemaVersion,
                ActivePageId = "main",
                Pages =
                [
                    new PageConfig { Id = "main", Widgets = [] },
                    new PageConfig { Id = "second", Name = "Second", Widgets = [] },
                ],
                Appearance = new AppearanceConfig { Theme = "dark" },
            };

            var settings = new FakeSettingsService
            {
                Handler = change =>
                {
                    if (change.Pages is { } pages)
                    {
                        config.Pages = [.. pages];
                    }

                    return SettingsApplyResult.Success(config);
                },
            };

            var viewModel = new PagesEditorViewModel(
                settings,
                new FakePageSwitchService(),
                () => config,
                imagePicker: new FakeImageFilePicker());
            var view = new PagesEditorView { DataContext = viewModel };
            var host = new Grid { Width = 420, Height = 700 };
            host.Children.Add(view);
            host.Measure(new Size(420, 700));
            host.Arrange(new Rect(0, 0, 420, 700));
            host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            viewModel.SelectPage("second");
            host.UpdateLayout();

            var picker = Assert.IsType<InlineColorPicker>(view.FindName("PageBackgroundColorPicker"));
            Assert.True(picker.AllowNone);
            SelectSwatch(host, picker, "#5B5BFF");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return new
            {
                Draft = viewModel.BackgroundColor,
                Saved = config.Pages[1].Background,
                Header = Assert.IsType<TextBlock>(picker.FindName("HeaderLabel")).Text,
            };
        });

        Assert.Equal("#5B5BFF", result.Draft);
        Assert.Equal("#5B5BFF", result.Saved);
        Assert.Equal("#5B5BFF", result.Header);
    }

    [Fact]
    public void Baml_AllFiveColourFieldsUseTheInlinePicker()
    {
        var root = FindRepositoryRoot();
        var overlay = File.ReadAllText(Path.Combine(root, "TouchPanel", "Views", "EditOverlay.xaml"));

        Assert.Contains("x:Name=\"ForegroundPicker\"", overlay, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BackgroundPicker\"", overlay, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccentPicker\"", overlay, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OnIconColorPicker\"", overlay, StringComparison.Ordinal);

        Assert.DoesNotContain("x:Name=\"ForegroundBox\"", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"BackgroundBox\"", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"AccentBox\"", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("OnIconColorBox", overlay, StringComparison.Ordinal);

        Assert.Contains("controls:InlineColorPicker x:Name=\"ForegroundPicker\"", overlay, StringComparison.Ordinal);
        Assert.Contains("controls:InlineColorPicker x:Name=\"BackgroundPicker\"", overlay, StringComparison.Ordinal);
        Assert.Contains("controls:InlineColorPicker x:Name=\"AccentPicker\"", overlay, StringComparison.Ordinal);
        Assert.Contains("controls:InlineColorPicker x:Name=\"OnIconColorPicker\"", overlay, StringComparison.Ordinal);
        Assert.Contains("AllowNone=\"True\"", overlay, StringComparison.Ordinal);

        var pages = File.ReadAllText(Path.Combine(root, "TouchPanel", "Views", "Settings", "PagesEditorView.xaml"));
        Assert.Contains("controls:InlineColorPicker x:Name=\"PageBackgroundColorPicker\"", pages, StringComparison.Ordinal);
        Assert.Contains("CommitCommand=\"{Binding ApplyBackgroundCommand}\"", pages, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"BackgroundColorBox\"", pages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Picker_FitsThePanelAtDesktopAndSmallTouchSizes()
    {
        using var sta = new StaThread();

        var sizes = new[] { (Width: 1024d, Height: 600d), (Width: 512d, Height: 300d) };

        foreach (var size in sizes)
        {
            var fits = await sta.InvokeAsync(() =>
            {
                // The property panel is 300 DIP wide with 12+16 padding and a 1 DIP border; the
                // picker's content column is ~270 DIP. Expanded, it must not request more width.
                var picker = new InlineColorPicker { IsExpanded = true, AllowNone = true };
                var host = new Grid { Width = 270, Height = size.Height };
                host.Children.Add(picker);
                host.Measure(new Size(270, size.Height));
                host.Arrange(new Rect(0, 0, 270, size.Height));
                host.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                host.UpdateLayout();

                var tiles = Assert.IsType<ItemsControl>(picker.FindName("SwatchTiles"));
                var buttons = FindVisualDescendants<Button>(tiles).ToArray();
                return new
                {
                    DesiredWidth = picker.DesiredSize.Width,
                    TileWidth = buttons.Min(button => button.ActualWidth),
                    TileHeight = buttons.Min(button => button.ActualHeight),
                    Header = Assert.IsType<Button>(picker.FindName("PickerHeader")).ActualHeight,
                };
            });

            Assert.True(fits.DesiredWidth <= 270, $"At {size.Width}x{size.Height} the picker requested {fits.DesiredWidth} DIP.");
            Assert.True(fits.TileWidth >= 40, $"At {size.Width}x{size.Height} a swatch was {fits.TileWidth} DIP wide.");
            Assert.True(fits.TileHeight >= 40, $"At {size.Width}x{size.Height} a swatch was {fits.TileHeight} DIP tall.");
            Assert.True(fits.Header >= 40, $"At {size.Width}x{size.Height} the header was {fits.Header} DIP tall.");
        }
    }

    private static Grid Host(FrameworkElement element, double width, double height)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        return host;
    }

    private static bool SelectSwatch(Grid host, InlineColorPicker picker, string hex)
    {
        var header = Assert.IsType<Button>(picker.FindName("PickerHeader"));
        if (!picker.IsExpanded)
        {
            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        host.UpdateLayout();

        var tiles = Assert.IsType<ItemsControl>(picker.FindName("SwatchTiles"));
        var tile = FindVisualDescendants<Button>(tiles)
            .Single(button => string.Equals((button.DataContext as ColorSwatchOption)?.Hex, hex, StringComparison.Ordinal));
        tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        return true;
    }

    private static EditorContent CreateEditor(WidgetConfig widget)
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages = [new PageConfig { Id = "main", Widgets = [widget] }],
        };

        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var store = new RecordingConfigStore();
        var service = new EditModeService(config, page, store, factory: registry, palette: registry);
        var overlay = new EditOverlay { DataContext = service };
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

        service.Enter(1024, 600);
        service.SelectWidget(page.Widgets[0]);
        service.OpenPropertyEditor();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

        var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
        var contentControl = Assert.IsType<ContentControl>(panel.Child);
        var content = (FrameworkElement)contentControl.ContentTemplate.LoadContent();
        var editor = Assert.IsType<PropertyEditorViewModel>(contentControl.Content);
        content.DataContext = editor;

        var host = new Grid { Width = 360, Height = 620 };
        host.Children.Add(content);
        host.Measure(new Size(360, 620));
        host.Arrange(new Rect(0, 0, 360, 620));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        host.UpdateLayout();

        return new EditorContent(editor, page.Widgets[0], content, host, service);
    }

    private static LabelWidgetConfig Label() => new()
    {
        Id = "l1",
        X = 0,
        Y = 0,
        Width = 200,
        Height = 100,
        Label = "Label",
    };

    private static ToggleWidgetConfig Toggle() => new()
    {
        Id = "toggle-1",
        X = 0,
        Y = 0,
        Width = 160,
        Height = 60,
        Label = "Mic",
        Icon = "power",
        OnIcon = "star",
        InitialOn = true,
        Action = new MuteActionConfig(),
    };

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(node, index);
                if (child is T match)
                {
                    yield return match;
                }

                pending.Enqueue(child);
            }
        }
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

    private sealed record EditorContent(
        PropertyEditorViewModel Editor,
        WidgetViewModelBase Widget,
        FrameworkElement Content,
        Grid Host,
        EditModeService Service);
}
