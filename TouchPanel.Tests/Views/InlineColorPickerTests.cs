using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
/// User-requested colour picker: parsed hex values, the 40 DIP field button that opens the docked
/// flyout, the flyout's swatch/None/hex apply path (one undo step per selection), the shared MRU
/// palette order, and the BAML wiring. No picker expands inside the property panel any more.
/// </summary>
public sealed class InlineColorPickerTests
{
    [Fact]
    public void Palette_IsTheFixedTwentyFourSwatchDefault()
    {
        Assert.Equal(24, ColorPalette.Standard.Count);
        Assert.Equal(RecentColorPalette.Capacity, ColorPalette.Standard.Count);
        Assert.Contains(ColorPalette.Standard, swatch => swatch.Hex == "#000000");
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
    public async Task Field_IsATouchSizedHeaderThatOpensTheFlyoutAndShowsTheSelection()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Label());
            var picker = Assert.IsType<InlineColorPicker>(context.Content.FindName("ForegroundPicker"));
            var header = Assert.IsType<Button>(picker.FindName("PickerHeader"));
            picker.SelectedHex = "#112233";
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var before = new
            {
                HeaderHeight = header.MinHeight,
                Label = Assert.IsType<TextBlock>(picker.FindName("HeaderLabel")).Text,
                Swatch = (Assert.IsType<Border>(picker.FindName("CurrentSwatch")).Background as SolidColorBrush)?.Color,
                Open = context.Editor.Flyout.IsOpen,
            };

            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var afterOpen = new
            {
                Open = context.Editor.Flyout.IsOpen,
                Kind = context.Editor.Flyout.Kind,
                Target = context.Editor.Flyout.Target,
            };

            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return new
            {
                before.HeaderHeight,
                before.Label,
                before.Swatch,
                BeforeOpen = before.Open,
                Opened = afterOpen.Open,
                afterOpen.Kind,
                afterOpen.Target,
                Closed = context.Editor.Flyout.IsOpen,
                HasInlineGrid = picker.FindName("SwatchTiles") is not null,
            };
        });

        Assert.True(result.HeaderHeight >= 40);
        Assert.Equal("#112233", result.Label);
        Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), result.Swatch);
        Assert.False(result.BeforeOpen);
        Assert.True(result.Opened);
        Assert.Equal(PropertyPickerKind.Color, result.Kind);
        Assert.Equal(PropertyPickerTarget.Foreground, result.Target);
        Assert.False(result.Closed, "Tapping the same colour field again must close the flyout.");
        Assert.False(result.HasInlineGrid, "The field must not host the swatch grid any more.");
    }

    [Fact]
    public async Task Flyout_SwatchAppliesTheExactHexAndClosesWithOneUndoStep()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Label());
            var editor = context.Editor;
            editor.Flyout.OpenColor(PropertyPickerTarget.Foreground);
            var tile = editor.Flyout.ColorTiles.Single(option => option.Hex == "#FF4D4D");
            editor.Flyout.SelectColorCommand.Execute(tile);

            var first = new
            {
                Config = ((LabelWidgetConfig)editor.Config).Foreground,
                Header = Assert.IsType<TextBlock>(Assert.IsType<InlineColorPicker>(
                    context.Content.FindName("ForegroundPicker")).FindName("HeaderLabel")).Text,
                Undo = context.Service.UndoDepth,
                Open = editor.Flyout.IsOpen,
                Inherited = editor.IsForegroundInherited,
            };

            editor.Flyout.OpenColor(PropertyPickerTarget.Foreground);
            var second = editor.Flyout.ColorTiles.Single(option => option.Hex == "#14B8A6");
            editor.Flyout.SelectColorCommand.Execute(second);

            return new
            {
                first,
                SecondConfig = ((LabelWidgetConfig)editor.Config).Foreground,
                SecondUndo = context.Service.UndoDepth,
            };
        });

        Assert.Equal("#FF4D4D", result.first.Config);
        Assert.Equal("#FF4D4D", result.first.Header);
        Assert.Equal(1, result.first.Undo);
        Assert.False(result.first.Open);
        Assert.False(result.first.Inherited);
        Assert.Equal("#14B8A6", result.SecondConfig);
        Assert.Equal(2, result.SecondUndo);
    }

    [Fact]
    public async Task Flyout_NoneClearsTheOverrideAndCloses()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Label());
            var editor = context.Editor;
            var widget = context.Widget;
            editor.ForegroundText = "#FF4D4D";

            editor.Flyout.OpenColor(PropertyPickerTarget.Foreground);
            var none = editor.Flyout.ColorTiles.Single(option => option.IsNone);
            editor.Flyout.SelectColorCommand.Execute(none);

            return new
            {
                Cleared = ((LabelWidgetConfig)editor.Config).Foreground,
                WidgetBrush = widget.ForegroundBrush,
                Inherited = editor.IsForegroundInherited,
                Open = editor.Flyout.IsOpen,
            };
        });

        Assert.Null(result.Cleared);
        Assert.Null(result.WidgetBrush);
        Assert.True(result.Inherited);
        Assert.False(result.Open);
    }

    [Theory]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("#123456", "#123456")]
    [InlineData("#80123456", "#80123456")]
    public async Task Flyout_HexInputAppliesThreeSixAndEightDigits(string input, string expected)
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Label());
            var editor = context.Editor;
            editor.Flyout.OpenColor(PropertyPickerTarget.Foreground);
            var host = Host(new PropertyFlyout { DataContext = editor.Flyout }, 380, 560);
            var flyout = Assert.IsType<PropertyFlyout>(host.Children[0]);
            Assert.IsType<TextBox>(flyout.FindName("FlyoutHexBox")).Text = input;

            var committed = flyout.CommitHex();
            return new { Committed = committed, editor.Flyout.SelectedHex, Config = ((LabelWidgetConfig)editor.Config).Foreground };
        });

        Assert.True(result.Committed);
        Assert.Equal(expected, result.Config);
        Assert.Equal(expected, result.SelectedHex);
    }

    [Fact]
    public async Task Flyout_InvalidHexIsRejectedWithFeedbackAndKeepsTheValue()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Label());
            var editor = context.Editor;
            editor.ForegroundText = "#112233";
            editor.Flyout.OpenColor(PropertyPickerTarget.Foreground);
            var host = Host(new PropertyFlyout { DataContext = editor.Flyout }, 380, 560);
            var flyout = Assert.IsType<PropertyFlyout>(host.Children[0]);
            Assert.IsType<TextBox>(flyout.FindName("FlyoutHexBox")).Text = "not-a-colour";

            var committed = flyout.CommitHex();
            var error = Assert.IsType<TextBlock>(flyout.FindName("FlyoutHexError"));

            return new
            {
                Committed = committed,
                Config = ((LabelWidgetConfig)editor.Config).Foreground,
                Error = error.Text,
                Visible = error.Visibility,
                Open = editor.Flyout.IsOpen,
            };
        });

        Assert.False(result.Committed);
        Assert.Equal("#112233", result.Config);
        Assert.Equal(PropertyPickerFlyoutViewModel.InvalidColorMessage, result.Error);
        Assert.Equal(Visibility.Visible, result.Visible);
        Assert.True(result.Open, "An invalid value must leave the flyout open for correction.");
    }

    [Fact]
    public async Task Flyout_PaletteOrderReflectsTheMostRecentSelection()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Label());
            var editor = context.Editor;
            var palette = new RecentColorPalette();
            var flyout = new PropertyPickerFlyoutViewModel(editor, palette);

            flyout.OpenColor(PropertyPickerTarget.Foreground);
            var initialFirst = flyout.ColorTiles[0].Hex;

            flyout.SelectColorCommand.Execute(flyout.ColorTiles.Single(tile => tile.Hex == "#14B8A6"));
            flyout.OpenColor(PropertyPickerTarget.Background);
            var afterFirst = flyout.ColorTiles.Select(tile => tile.Hex).ToArray();

            flyout.SelectColorCommand.Execute(flyout.ColorTiles.Single(tile => tile.Hex == "#FF4D4D"));
            flyout.OpenColor(PropertyPickerTarget.Foreground);
            var afterSecond = flyout.ColorTiles.Select(tile => tile.Hex).ToArray();

            return new { initialFirst, afterFirst, afterSecond };
        });

        Assert.Equal(ColorPalette.Standard[0].Hex, result.initialFirst);
        Assert.Equal("#14B8A6", result.afterFirst[0]);
        Assert.Equal("#FF4D4D", result.afterSecond[0]);
        Assert.Equal("#14B8A6", result.afterSecond[1]);
    }

    [Fact]
    public async Task ToggleEditor_FlyoutAppliesTheOnIconColourLive()
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

            editor.Flyout.OpenColor(PropertyPickerTarget.OnIconColor);
            editor.Flyout.SelectColorCommand.Execute(
                editor.Flyout.ColorTiles.Single(tile => tile.Hex == "#FFB000"));

            return new
            {
                SectionVisibility = section.Visibility,
                Supports = editor.SupportsOnIconColor,
                Config = ((ToggleWidgetConfig)editor.Config).OnIconColor,
                WidgetColor = (widget.OnIconBrush as SolidColorBrush)?.Color,
                Undo = context.Service.UndoDepth,
                Inherited = editor.IsOnIconColorInherited,
            };
        });

        Assert.Equal(Visibility.Visible, result.SectionVisibility);
        Assert.True(result.Supports);
        Assert.Equal("#FFB000", result.Config);
        Assert.Equal(Color.FromRgb(0xFF, 0xB0, 0x00), result.WidgetColor);
        Assert.Equal(1, result.Undo);
        Assert.False(result.Inherited);
    }

    [Fact]
    public async Task PagesEditor_BackgroundFlyoutAppliesThePageBackground()
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

            var palette = new RecentColorPalette();
            var viewModel = new PagesEditorViewModel(
                settings,
                new FakePageSwitchService(),
                () => config,
                imagePicker: new FakeImageFilePicker(),
                recentColors: palette);
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

            viewModel.ToggleBackgroundColorFlyoutCommand.Execute(null);
            var flyout = viewModel.BackgroundColorFlyout;
            flyout.SelectColorCommand.Execute(flyout.ColorTiles.Single(tile => tile.Hex == "#5B5BFF"));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return new
            {
                Draft = viewModel.BackgroundColor,
                Saved = config.Pages[1].Background,
                Header = Assert.IsType<TextBlock>(picker.FindName("HeaderLabel")).Text,
                FlyoutOpen = flyout.IsOpen,
                FirstRecent = palette.Colors[0].Hex,
            };
        });

        Assert.Equal("#5B5BFF", result.Draft);
        Assert.Equal("#5B5BFF", result.Saved);
        Assert.Equal("#5B5BFF", result.Header);
        Assert.False(result.FlyoutOpen);
        Assert.Equal("#5B5BFF", result.FirstRecent);
    }

    [Fact]
    public void Baml_AllColourFieldsUseTheFieldButtonAndTheDockedFlyout()
    {
        var root = FindRepositoryRoot();
        var overlay = File.ReadAllText(Path.Combine(root, "TouchPanel", "Views", "EditOverlay.xaml"));

        Assert.Contains("x:Name=\"ForegroundPicker\"", overlay, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BackgroundPicker\"", overlay, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccentPicker\"", overlay, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OnIconColorPicker\"", overlay, StringComparison.Ordinal);
        Assert.Contains("FieldCommand=\"{Binding ToggleColorFlyoutCommand}\"", overlay, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PropertyFlyoutHost\"", overlay, StringComparison.Ordinal);
        Assert.Contains("controls:PropertyFlyout", overlay, StringComparison.Ordinal);

        Assert.DoesNotContain("x:Name=\"ForegroundBox\"", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"BackgroundBox\"", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"AccentBox\"", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("OnIconColorBox", overlay, StringComparison.Ordinal);

        var picker = File.ReadAllText(Path.Combine(root, "TouchPanel", "Views", "Controls", "InlineColorPicker.xaml"));
        Assert.DoesNotContain("SwatchTiles", picker, StringComparison.Ordinal);
        Assert.DoesNotContain("HexBox", picker, StringComparison.Ordinal);
        Assert.Contains("PickerHeader", picker, StringComparison.Ordinal);

        var flyout = File.ReadAllText(Path.Combine(root, "TouchPanel", "Views", "Controls", "PropertyFlyout.xaml"));
        Assert.Contains("FlyoutColorTiles", flyout, StringComparison.Ordinal);
        Assert.Contains("FlyoutHexBox", flyout, StringComparison.Ordinal);
        Assert.Contains("TouchPan.IsEnabled", flyout, StringComparison.Ordinal);

        var pages = File.ReadAllText(Path.Combine(root, "TouchPanel", "Views", "Settings", "PagesEditorView.xaml"));
        Assert.Contains("controls:InlineColorPicker x:Name=\"PageBackgroundColorPicker\"", pages, StringComparison.Ordinal);
        Assert.Contains("ToggleBackgroundColorFlyoutCommand", pages, StringComparison.Ordinal);
        Assert.Contains("ColorPickerFlyout", pages, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"BackgroundColorBox\"", pages, StringComparison.Ordinal);
    }

    private static Grid Host(FrameworkElement element, double width, double height)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        host.UpdateLayout();
        return host;
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
