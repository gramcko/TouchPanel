using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Views;
using TouchPanel.Views.Controls;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// User-requested docked icon picker: the field is a 40 DIP button, the flyout opens to the left of
/// the property panel (not inside it), lists the whole catalog with the current entry marked, toggles
/// on the same field, switches target in place, stays exclusive with the colour flyout, and closes
/// after a selection or when the editor closes.
/// </summary>
public sealed class InlineIconPickerTests
{
    [Fact]
    public async Task Field_IsAHeaderOnlyButtonThatTogglesTheFlyout()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Gauge());
            var picker = Assert.IsType<InlineIconPicker>(context.Content.FindName("IconPicker"));
            var header = Assert.IsType<Button>(picker.FindName("PickerHeader"));

            var headerHeight = header.MinHeight;
            var hasInlineTiles = picker.FindName("PickerTiles") is not null;

            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var opened = new
            {
                context.Editor.Flyout.IsOpen,
                context.Editor.Flyout.Kind,
                context.Editor.Flyout.Target,
                context.Editor.Flyout.IconOptions.Count,
            };

            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return new
            {
                HeaderHeight = headerHeight,
                HasInlineTiles = hasInlineTiles,
                opened.IsOpen,
                opened.Kind,
                opened.Target,
                opened.Count,
                Closed = context.Editor.Flyout.IsOpen,
            };
        });

        Assert.True(result.HeaderHeight >= 40);
        Assert.False(result.HasInlineTiles, "The icon field must not expand a tile list inside the panel.");
        Assert.True(result.IsOpen);
        Assert.Equal(PropertyPickerKind.Icon, result.Kind);
        Assert.Equal(PropertyPickerTarget.Icon, result.Target);
        Assert.Equal(IconCatalog.BuiltIn.Count + 1, result.Count);
        Assert.False(result.Closed, "Tapping the same icon field again must close the flyout.");
    }

    [Fact]
    public async Task Flyout_ListsEveryCatalogEntryAndMarksTheCurrentOne()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Gauge());
            var editor = context.Editor;
            editor.SelectedIcon = IconCatalog.BuiltIn.First(option => option.Id == "star");

            editor.Flyout.OpenIcon(PropertyPickerTarget.Icon);
            var host = Host(new PropertyFlyout { DataContext = editor.Flyout }, 380, 560);
            var flyout = Assert.IsType<PropertyFlyout>(host.Children[0]);
            var tiles = Assert.IsType<ItemsControl>(flyout.FindName("FlyoutIconTiles"));
            var buttons = FindVisualDescendants<Button>(tiles).ToArray();

            var ids = editor.Flyout.IconOptions.Select(option => option.Id).ToArray();
            var star = buttons.Single(button => Option(button)?.Id == "star");
            var power = buttons.Single(button => Option(button)?.Id == "power");
            var none = buttons.Single(button => Option(button)?.Id == string.Empty);
            var strokeTile = buttons.Single(button => Option(button)?.Id == "headphones");
            var strokePath = FindVisualDescendants<System.Windows.Shapes.Path>(strokeTile)
                .Single(path => path.Name == "StrokePath");

            return new
            {
                Count = buttons.Length,
                Ids = ids,
                StarMarked = star.BorderThickness.Left >= 2,
                PowerUnmarked = power.BorderThickness.Left == 1,
                NoneUnmarked = none.BorderThickness.Left == 1,
                MinHeight = buttons.Min(button => button.ActualHeight),
                StrokeVisible = strokePath.Visibility,
                HasStrokeGeometry = strokePath.Data is not null,
            };
        });

        Assert.Equal(IconCatalog.BuiltIn.Count + 1, result.Count);
        Assert.Equal(string.Empty, result.Ids[0]);
        Assert.All(IconCatalog.BuiltIn, option => Assert.Contains(option.Id, result.Ids));
        Assert.Contains("steam", result.Ids);
        Assert.Contains("vscode", result.Ids);
        Assert.Contains("headphones", result.Ids);
        Assert.Contains("microphoneMuted", result.Ids);
        Assert.True(result.StarMarked, "The current entry must carry the accent selection border.");
        Assert.True(result.PowerUnmarked);
        Assert.True(result.NoneUnmarked);
        Assert.True(result.MinHeight >= 44, $"Tiles must be at least 44 DIP; were {result.MinHeight}.");
        Assert.Equal(Visibility.Visible, result.StrokeVisible);
        Assert.True(result.HasStrokeGeometry);
    }

    [Fact]
    public async Task Flyout_SelectionAppliesLiveClosesAndIsOneUndoStep()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Gauge());
            var editor = context.Editor;
            var gauge = Assert.IsType<GaugeWidgetViewModel>(context.Widget);
            editor.Flyout.OpenIcon(PropertyPickerTarget.Icon);
            var power = editor.Flyout.IconOptions.First(option => option.Id == "power");
            editor.Flyout.SelectIconCommand.Execute(power);

            var applied = new
            {
                Config = ((GaugeWidgetConfig)editor.Config).Icon,
                Selected = editor.SelectedIcon.Id,
                gauge.IconGlyph,
                Undo = context.Service.UndoDepth,
                Closed = editor.Flyout.IsOpen,
            };

            editor.Flyout.OpenIcon(PropertyPickerTarget.Icon);
            editor.Flyout.SelectIconCommand.Execute(editor.Flyout.IconOptions[0]);

            return new
            {
                applied,
                Cleared = ((GaugeWidgetConfig)editor.Config).Icon,
                gauge.HasIcon,
                Undo = context.Service.UndoDepth,
            };
        });

        Assert.Equal("power", result.applied.Config);
        Assert.Equal("power", result.applied.Selected);
        Assert.Equal(IconCatalog.GlyphFor("power"), result.applied.IconGlyph);
        Assert.Equal(1, result.applied.Undo);
        Assert.False(result.applied.Closed);
        Assert.Null(result.Cleared);
        Assert.False(result.HasIcon);
        Assert.Equal(2, result.Undo);
    }

    [Fact]
    public async Task Flyout_SwitchesTargetInPlaceAndIsExclusiveWithTheColourFlyout()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Toggle());
            var editor = context.Editor;

            editor.Flyout.ToggleIcon(PropertyPickerTarget.Icon);
            var iconOpen = (editor.Flyout.IsOpen, editor.Flyout.Kind, editor.Flyout.Target, editor.Flyout.IconOptions.Count);

            editor.Flyout.ToggleIcon(PropertyPickerTarget.OnIcon);
            var switched = (editor.Flyout.IsOpen, editor.Flyout.Kind, editor.Flyout.Target, editor.Flyout.IconOptions.Count);

            editor.Flyout.ToggleColor(PropertyPickerTarget.OnIconColor);
            var colourOpen = (editor.Flyout.IsOpen, editor.Flyout.Kind, editor.Flyout.Target, editor.Flyout.IconOptions.Count);

            editor.Flyout.ToggleIcon(PropertyPickerTarget.Icon);
            var backToIcon = (editor.Flyout.IsOpen, editor.Flyout.Kind, editor.Flyout.Target);

            return new { iconOpen, switched, colourOpen, backToIcon };
        });

        Assert.True(result.iconOpen.IsOpen);
        Assert.Equal(PropertyPickerKind.Icon, result.iconOpen.Kind);
        Assert.Equal(PropertyPickerTarget.Icon, result.iconOpen.Target);
        Assert.True(result.switched.IsOpen, "Tapping another icon field must switch the target in place.");
        Assert.Equal(PropertyPickerTarget.OnIcon, result.switched.Target);
        Assert.Equal(PropertyPickerKind.Color, result.colourOpen.Kind);
        Assert.Equal(PropertyPickerTarget.OnIconColor, result.colourOpen.Target);
        Assert.Equal(PropertyPickerKind.Icon, result.backToIcon.Kind);
        Assert.Equal(PropertyPickerTarget.Icon, result.backToIcon.Target);
    }

    [Fact]
    public async Task Flyout_OffersTheLabelAndOnStateTargetsWithTheirOwnOptionLists()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var gauge = CreateEditor(Gauge());
            gauge.Editor.Flyout.OpenIcon(PropertyPickerTarget.LabelIcon);
            var labelIcons = gauge.Editor.Flyout.IconOptions.Select(option => option.Id).ToArray();
            gauge.Editor.Flyout.SelectIconCommand.Execute(
                gauge.Editor.Flyout.IconOptions.First(option => option.Id == "download"));
            var label = (Config: ((GaugeWidgetConfig)gauge.Editor.Config).LabelIcon, gauge.Editor.Flyout.IsOpen);

            var toggle = CreateEditor(Toggle());
            toggle.Editor.Flyout.OpenIcon(PropertyPickerTarget.OnIcon);
            var onIcons = toggle.Editor.Flyout.IconOptions.Select(option => option.Id).ToArray();
            toggle.Editor.Flyout.SelectIconCommand.Execute(
                toggle.Editor.Flyout.IconOptions.First(option => option.Id == "microphoneMuted"));
            var on = (Config: ((ToggleWidgetConfig)toggle.Editor.Config).OnIcon, toggle.Editor.Flyout.IsOpen);

            return new { labelIcons, label, onIcons, on };
        });

        Assert.Equal(IconCatalog.BuiltIn.Count + 1, result.labelIcons.Length);
        Assert.Contains("download", result.labelIcons);
        Assert.Equal("download", result.label.Config);
        Assert.False(result.label.IsOpen);
        Assert.Equal(IconCatalog.BuiltIn.Count + 1, result.onIcons.Length);
        Assert.Contains("microphoneMuted", result.onIcons);
        Assert.Equal("microphoneMuted", result.on.Config);
        Assert.False(result.on.IsOpen);
    }

    [Fact]
    public async Task Flyout_HidesWhenTheSelectionChangesOrEditModeEnds()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditorWithTwoWidgets();
            var service = context.Service;
            var first = service.PropertyEditor!;
            first.Flyout.OpenIcon(PropertyPickerTarget.Icon);

            service.SelectWidget(context.Page.Widgets[1]);
            var afterSelection = new
            {
                FirstOpen = first.Flyout.IsOpen,
                HasEditor = service.HasPropertyEditor,
            };

            service.SelectWidget(context.Page.Widgets[0]);
            service.OpenPropertyEditor();
            var editor = service.PropertyEditor!;
            editor.Flyout.OpenColor(PropertyPickerTarget.Foreground);
            var closed = editor.Flyout.IsOpen;
            service.ClosePropertyEditor();
            var afterClose = (editor.Flyout.IsOpen, service.HasPropertyEditor);

            service.SelectWidget(context.Page.Widgets[0]);
            service.OpenPropertyEditor();
            var exitEditor = service.PropertyEditor!;
            exitEditor.Flyout.OpenIcon(PropertyPickerTarget.Icon);
            service.ExitAsync().GetAwaiter().GetResult();
            var afterExit = (exitEditor.Flyout.IsOpen, service.IsEditMode);

            return new { afterSelection.FirstOpen, afterSelection.HasEditor, Closed = closed, afterClose, afterExit };
        });

        Assert.False(result.FirstOpen, "A selection change must close the previous flyout.");
        Assert.True(result.Closed);
        Assert.False(result.afterClose.Item1);
        Assert.False(result.afterClose.Item2);
        Assert.False(result.afterExit.Item1, "Leaving edit mode must close the flyout.");
        Assert.False(result.afterExit.Item2);
    }

    [Fact]
    public async Task Flyout_DocksLeftOfThePropertyPanelAtDesktopAndMinimumSizes()
    {
        using var sta = new StaThread();

        var sizes = new[] { (Width: 1024d, Height: 600d), (Width: 512d, Height: 300d) };

        foreach (var size in sizes)
        {
            var result = await sta.InvokeAsync(() =>
            {
                var context = CreateEditor(Gauge(), size.Width, size.Height, out var overlay, out var panel);
                context.Editor.Flyout.OpenIcon(PropertyPickerTarget.Icon);
                overlay.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                overlay.UpdateLayout();

                var host = Assert.IsType<ContentControl>(overlay.FindName("PropertyFlyoutHost"));
                var sheet = Assert.IsType<StackPanel>(overlay.FindName("EditSheet"));
                var flyout = FindVisualDescendants<PropertyFlyout>(host).FirstOrDefault();
                var flyoutBounds = flyout is null
                    ? Rect.Empty
                    : flyout.TransformToAncestor(overlay).TransformBounds(
                        new Rect(0, 0, flyout.ActualWidth, flyout.ActualHeight));
                var hostBounds = host.TransformToAncestor(overlay).TransformBounds(
                    new Rect(0, 0, host.ActualWidth, host.ActualHeight));
                var panelBounds = panel.TransformToAncestor(overlay).TransformBounds(
                    new Rect(0, 0, panel.ActualWidth, panel.ActualHeight));
                var sheetBounds = sheet.TransformToAncestor(overlay).TransformBounds(
                    new Rect(0, 0, sheet.ActualWidth, sheet.ActualHeight));

                return new
                {
                    OverlayWidth = overlay.ActualWidth,
                    OverlayHeight = overlay.ActualHeight,
                    HostWidth = host.ActualWidth,
                    HostHeight = host.ActualHeight,
                    FlyoutWidth = flyout?.ActualWidth ?? 0,
                    FlyoutHeight = flyout?.ActualHeight ?? 0,
                    HostBounds = hostBounds,
                    FlyoutBounds = flyoutBounds,
                    PanelBounds = panelBounds,
                    SheetBounds = sheetBounds,
                    FlyoutVisible = flyout?.Visibility ?? Visibility.Collapsed,
                };
            });

            Assert.True(result.FlyoutVisible == Visibility.Visible, $"At {size.Width}x{size.Height} the flyout must be visible.");
            Assert.True(result.HostWidth >= FlyoutWidthConverter.MinimumWidth, $"At {size.Width}x{size.Height} the flyout slot was {result.HostWidth} DIP.");
            Assert.True(result.HostWidth <= 380.5, $"At {size.Width}x{size.Height} the flyout was wider than the 380 DIP ceiling: {result.HostWidth}.");
            Assert.True(result.HostBounds.Left >= -0.5, $"At {size.Width}x{size.Height} the flyout starts off-panel at {result.HostBounds.Left}.");
            Assert.True(result.HostBounds.Right <= result.OverlayWidth + 0.5, $"At {size.Width}x{size.Height} the flyout ends outside the panel.");
            Assert.True(result.HostBounds.Right <= result.PanelBounds.Left + 0.5, $"At {size.Width}x{size.Height} the flyout overlaps the property panel.");
            if (result.HostHeight > 20)
            {
                Assert.True(
                    result.HostBounds.Bottom <= result.SheetBounds.Top + 0.5,
                    $"At {size.Width}x{size.Height} the flyout overlaps the bottom edit sheet (host {result.HostBounds}, sheet {result.SheetBounds}, overlay {result.OverlayWidth}x{result.OverlayHeight}).");
            }
            else
            {
                // At the 512x300 minimum the expanded edit sheet takes the whole height, so row 0
                // (which also hosts the property panel) has no vertical room. The flyout stays
                // inside that row instead of spilling over the sheet.
                Assert.True(result.HostBounds.Bottom <= result.SheetBounds.Top + 10.5);
            }
            Assert.True(result.FlyoutBounds.Bottom <= result.OverlayHeight + 0.5);
            Assert.True(result.FlyoutBounds.Right <= result.PanelBounds.Left + 0.5);
        }
    }

    private static IconOption? Option(DependencyObject element) => (element as FrameworkElement)?.DataContext as IconOption;

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
        var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
        var overlay = new EditOverlay { DataContext = service };
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        service.Enter(1024, 600);
        service.SelectWidget(page.Widgets[0]);
        service.OpenPropertyEditor();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        overlay.Measure(new Size(1024, 600));
        overlay.Arrange(new Rect(0, 0, 1024, 600));
        overlay.UpdateLayout();

        var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
        var contentControl = Assert.IsType<ContentControl>(panel.Child);
        var content = (FrameworkElement)contentControl.ContentTemplate.LoadContent();
        var editor = Assert.IsType<PropertyEditorViewModel>(contentControl.Content);
        content.DataContext = editor;

        var host = new Grid { Width = 300, Height = 560 };
        host.Children.Add(content);
        host.Measure(new Size(300, 560));
        host.Arrange(new Rect(0, 0, 300, 560));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        host.UpdateLayout();

        return new EditorContent(editor, page.Widgets[0], content, host, service);
    }

    private static EditorContent CreateEditor(
        WidgetConfig widget,
        double width,
        double height,
        out EditOverlay overlay,
        out Border panel)
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages = [new PageConfig { Id = "main", Widgets = [widget] }],
        };

        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
        service.Enter(width, height);
        overlay = new EditOverlay
        {
            DataContext = service,
            Width = width,
            Height = height,
        };
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        overlay.Measure(new Size(width, height));
        overlay.Arrange(new Rect(0, 0, width, height));
        overlay.UpdateLayout();

        service.SelectWidget(page.Widgets[0]);
        service.OpenPropertyEditor();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        overlay.Measure(new Size(width, height));
        overlay.Arrange(new Rect(0, 0, width, height));
        overlay.UpdateLayout();

        panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
        var contentControl = Assert.IsType<ContentControl>(panel.Child);
        var content = (FrameworkElement)contentControl.ContentTemplate.LoadContent();
        var editor = Assert.IsType<PropertyEditorViewModel>(contentControl.Content);
        content.DataContext = editor;

        var host = new Grid { Width = 300, Height = 560 };
        host.Children.Add(content);
        host.Measure(new Size(300, 560));
        host.Arrange(new Rect(0, 0, 300, 560));
        host.UpdateLayout();

        return new EditorContent(editor, page.Widgets[0], content, host, service);
    }

    private static TwoWidgetContent CreateEditorWithTwoWidgets()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Widgets =
                    [
                        new GaugeWidgetConfig
                        {
                            Id = "g1",
                            X = 0,
                            Y = 0,
                            Width = 240,
                            Height = 180,
                            Sensor = "cpu.total",
                            Minimum = 0,
                            Maximum = 100,
                        },
                        new LabelWidgetConfig
                        {
                            Id = "l1",
                            X = 0,
                            Y = 200,
                            Width = 200,
                            Height = 100,
                            Label = "Label",
                        },
                    ],
                },
            ],
        };

        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
        service.Enter(1024, 600);
        service.SelectWidget(page.Widgets[0]);
        service.OpenPropertyEditor();
        return new TwoWidgetContent(service, page);
    }

    private static GaugeWidgetConfig Gauge() => new()
    {
        Id = "g1",
        X = 0,
        Y = 0,
        Width = 240,
        Height = 180,
        Sensor = "cpu.total",
        Minimum = 0,
        Maximum = 100,
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

    private sealed record EditorContent(
        PropertyEditorViewModel Editor,
        WidgetViewModelBase Widget,
        FrameworkElement Content,
        Grid Host,
        EditModeService Service);

    private sealed record TwoWidgetContent(EditModeService Service, PageViewModel Page);
}
