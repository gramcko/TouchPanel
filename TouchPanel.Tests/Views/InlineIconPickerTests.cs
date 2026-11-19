using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Views;
using TouchPanel.Views.Controls;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// User-requested replacement for the long icon ComboBoxes: the property panel now hosts an inline
/// <see cref="InlineIconPicker"/> (collapsed header + two-column tile list in the panel flow). These
/// STA checks cover the collapsed/expanded states, catalog coverage, selection marking, the
/// apply/clear paths, that the tiles live inside the panel's <see cref="ScrollViewer"/> (so the
/// existing <see cref="TouchPan"/> behavior scrolls over them), and that the popup ComboBox
/// touch-scroll regression is fully removed.
/// </summary>
public sealed class InlineIconPickerTests
{
    [Fact]
    public async Task IconPickers_StartCollapsedAndOfferTheWholeCatalog()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var editor = CreateEditorContent(Gauge());
            var icon = Assert.IsType<InlineIconPicker>(editor.Content.FindName("IconPicker"));
            var labelIcon = Assert.IsType<InlineIconPicker>(editor.Content.FindName("LabelIconPicker"));
            var tiles = Assert.IsType<ItemsControl>(icon.FindName("PickerTiles"));

            return new
            {
                IconExpanded = icon.IsExpanded,
                LabelExpanded = labelIcon.IsExpanded,
                TilesVisibility = tiles.Visibility,
                IconIds = Options(icon).Select(option => option.Id).ToArray(),
                LabelIds = Options(labelIcon).Select(option => option.Id).ToArray(),
            };
        });

        Assert.False(result.IconExpanded, "The icon picker must start collapsed.");
        Assert.False(result.LabelExpanded, "The label-icon picker must start collapsed.");
        Assert.Equal(Visibility.Collapsed, result.TilesVisibility);

        // "None" plus every built-in catalog entry, for both lists (LabelIconOptions reuses the
        // IconOptions list, including the existing "(none)" entry).
        Assert.Equal(IconCatalog.BuiltIn.Count + 1, result.IconIds.Length);
        Assert.Equal(IconCatalog.BuiltIn.Count + 1, result.LabelIds.Length);
        Assert.Equal(string.Empty, result.IconIds[0]);
        Assert.All(IconCatalog.BuiltIn, option => Assert.Contains(option.Id, result.IconIds));
        Assert.All(IconCatalog.BuiltIn, option => Assert.Contains(option.Id, result.LabelIds));
        Assert.Contains("steam", result.IconIds);
        Assert.Contains("vscode", result.IconIds);
        Assert.Contains("github", result.LabelIds);
    }

    [Fact]
    public async Task IconPicker_ExpandsListsEveryEntryMarksTheSelectionAndCollapsesAfterSelecting()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditorContent(Gauge());
            var editor = context.Editor;
            var gauge = Assert.IsType<GaugeWidgetViewModel>(context.Widget);

            // Select a known entry through the view model; the picker must show it in the header and
            // mark its tile.
            editor.SelectedIcon = IconCatalog.BuiltIn.First(option => option.Id == "star");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var picker = Assert.IsType<InlineIconPicker>(context.Content.FindName("IconPicker"));
            var header = Assert.IsType<Button>(picker.FindName("PickerHeader"));
            var tiles = Assert.IsType<ItemsControl>(picker.FindName("PickerTiles"));

            var collapsedInitially = picker.IsExpanded;
            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            context.Host.UpdateLayout();

            var expanded = picker.IsExpanded;
            var tilesVisible = tiles.Visibility;
            var tileButtons = FindVisualDescendants<Button>(tiles).ToArray();
            var starTile = tileButtons.Single(button => Option(button)?.Id == "star");
            var powerTile = tileButtons.Single(button => Option(button)?.Id == "power");
            var noneTile = tileButtons.Single(button => Option(button)?.Id == string.Empty);
            var starMarked = starTile.BorderThickness.Left >= 2;
            var powerUnmarked = powerTile.BorderThickness.Left == 1;
            var noneUnmarked = noneTile.BorderThickness.Left == 1;
            var minimumTileHeight = tileButtons.Min(button => button.ActualHeight);
            var headerTexts = FindVisualDescendants<TextBlock>(header).Select(text => text.Text).ToArray();

            // The header toggles closed again.
            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            var collapsedAgain = picker.IsExpanded;

            // Re-open, tap a different tile: it applies, and the list collapses.
            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            powerTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var headerTextsAfterSelection = FindVisualDescendants<TextBlock>(header).Select(text => text.Text).ToArray();

            return new
            {
                collapsedInitially,
                expanded,
                tilesVisible,
                TileCount = tileButtons.Length,
                starMarked,
                powerUnmarked,
                noneUnmarked,
                minimumTileHeight,
                headerTexts,
                collapsedAgain,
                CollapsedAfterSelect = picker.IsExpanded,
                PickerSelection = picker.SelectedItem?.Id,
                EditorSelection = editor.SelectedIcon.Id,
                WidgetGlyph = gauge.IconGlyph,
                AppliedToConfig = ((GaugeWidgetConfig)editor.Config).Icon,
                headerTextsAfterSelection,
            };
        });

        Assert.False(result.collapsedInitially);
        Assert.True(result.expanded, "Tapping the header must expand the inline list.");
        Assert.Equal(Visibility.Visible, result.tilesVisible);
        Assert.Equal(IconCatalog.BuiltIn.Count + 1, result.TileCount);
        Assert.True(result.starMarked, "The current entry must carry the accent selection border.");
        Assert.True(result.powerUnmarked, "Unselected entries must keep the neutral border.");
        Assert.True(result.noneUnmarked, "Unselected entries must keep the neutral border.");
        Assert.True(result.minimumTileHeight >= 44, $"Tiles must be at least 44 DIP tall; was {result.minimumTileHeight}.");
        Assert.Contains("Star", result.headerTexts);
        Assert.Contains(IconCatalog.GlyphFor("star"), result.headerTexts);
        Assert.False(result.collapsedAgain, "Tapping the header again must collapse the list.");
        Assert.False(result.CollapsedAfterSelect, "Selecting a tile must collapse the list.");
        Assert.Equal("power", result.PickerSelection);
        Assert.Equal("power", result.EditorSelection);
        Assert.Equal("power", result.AppliedToConfig);
        Assert.Equal(IconCatalog.GlyphFor("power"), result.WidgetGlyph);
        Assert.Contains("Power", result.headerTextsAfterSelection);
        Assert.Contains(IconCatalog.GlyphFor("power"), result.headerTextsAfterSelection);
    }

    [Fact]
    public async Task LabelIconPicker_SelectionAppliesLiveAndNoneClearsIt()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditorContent(Bar());
            var editor = context.Editor;
            var picker = Assert.IsType<InlineIconPicker>(context.Content.FindName("LabelIconPicker"));
            var header = Assert.IsType<Button>(picker.FindName("PickerHeader"));
            var tiles = Assert.IsType<ItemsControl>(picker.FindName("PickerTiles"));
            var barViewModel = Assert.IsType<BarWidgetViewModel>(context.Widget);

            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            context.Host.UpdateLayout();

            var downloadTile = FindVisualDescendants<Button>(tiles).Single(button => Option(button)?.Id == "download");
            downloadTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var applied = new
            {
                Config = ((BarWidgetConfig)editor.Config).LabelIcon,
                Selected = editor.SelectedLabelIcon.Id,
                Picker = picker.SelectedItem?.Id,
                barViewModel.HasLabelIcon,
                barViewModel.LabelIconGlyph,
                barViewModel.Label,
                editor.LabelText,
                Collapsed = picker.IsExpanded,
            };

            // Re-open and pick "None": the configured label icon clears and the text label returns.
            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            var noneTile = FindVisualDescendants<Button>(tiles).Single(button => Option(button)?.Id == string.Empty);
            noneTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return new
            {
                Applied = applied,
                Cleared = new
                {
                    Config = ((BarWidgetConfig)editor.Config).LabelIcon,
                    Selected = editor.SelectedLabelIcon.Id,
                    Picker = picker.SelectedItem?.Id,
                    barViewModel.HasLabelIcon,
                    barViewModel.LabelIconGlyph,
                    barViewModel.Label,
                    editor.LabelText,
                    Collapsed = picker.IsExpanded,
                },
            };
        });

        Assert.Equal("download", result.Applied.Config);
        Assert.Equal("download", result.Applied.Selected);
        Assert.Equal("download", result.Applied.Picker);
        Assert.True(result.Applied.HasLabelIcon);
        Assert.Equal(IconCatalog.GlyphFor("download"), result.Applied.LabelIconGlyph);
        Assert.Equal("bar1", result.Applied.Label);
        Assert.Equal("bar1", result.Applied.LabelText);
        Assert.False(result.Applied.Collapsed);

        Assert.Null(result.Cleared.Config);
        Assert.Equal(string.Empty, result.Cleared.Selected);
        Assert.Equal(string.Empty, result.Cleared.Picker);
        Assert.False(result.Cleared.HasLabelIcon);
        Assert.Null(result.Cleared.LabelIconGlyph);
        Assert.Equal("bar1", result.Cleared.Label);
        Assert.Equal("bar1", result.Cleared.LabelText);
        Assert.False(result.Cleared.Collapsed);
    }

    [Fact]
    public async Task ExpandedPicker_IsInsideThePropertyPanelScrollAndPansOverTheTiles()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditorContent(Gauge());
            var scroll = Assert.IsType<ScrollViewer>(context.Content.FindName("PropertyPanelScroll"));
            var picker = Assert.IsType<InlineIconPicker>(context.Content.FindName("IconPicker"));
            var header = Assert.IsType<Button>(picker.FindName("PickerHeader"));
            var tiles = Assert.IsType<ItemsControl>(picker.FindName("PickerTiles"));

            var collapsedInsideScroll = IsVisualDescendant(picker, scroll);

            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            context.Host.UpdateLayout();

            var firstTile = FindVisualDescendants<Button>(tiles).First();
            var expandedInsideScroll = IsVisualDescendant(firstTile, scroll);
            var behavior = TouchPan.GetBehaviorForTest(scroll);
            var scrollable = scroll.ScrollableHeight;

            // A tap-sized move does not pan, so the tile keeps the gesture for selection.
            var tapTracked = behavior!.BeginForTest(150, 300, firstTile);
            var tapPanned = behavior.MoveForTest(151, 301).IsPanning;
            var tapEndedAsPan = behavior.EndForTest();
            var offsetAfterTap = scroll.VerticalOffset;

            // A vertical drag that starts on a tile must pan the panel (never be excluded).
            var dragTracked = behavior.BeginForTest(150, 400, firstTile);
            var dragPanned = behavior.MoveForTest(150, 340).IsPanning;
            scroll.UpdateLayout();
            var offsetAfterDrag = scroll.VerticalOffset;
            var dragEndedAsPan = behavior.EndForTest();

            return new
            {
                TouchPanEnabled = TouchPan.GetIsEnabled(scroll),
                collapsedInsideScroll,
                expandedInsideScroll,
                HasBehavior = behavior is not null,
                scrollable,
                tapTracked,
                tapPanned,
                tapEndedAsPan,
                offsetAfterTap,
                dragTracked,
                dragPanned,
                offsetAfterDrag,
                dragEndedAsPan,
            };
        });

        Assert.True(result.TouchPanEnabled, "The property panel must keep the panel-level touch pan.");
        Assert.True(result.HasBehavior, "The TouchPan behavior must be attached to the panel's viewer.");
        Assert.True(result.collapsedInsideScroll, "The picker must be inside the property panel's ScrollViewer.");
        Assert.True(result.expandedInsideScroll, "Expanded tiles must be inside the property panel's ScrollViewer.");
        Assert.True(result.scrollable > 0, "The expanded panel must be scrollable.");

        Assert.True(result.tapTracked);
        Assert.False(result.tapPanned);
        Assert.False(result.tapEndedAsPan);
        Assert.Equal(0, result.offsetAfterTap);

        Assert.True(result.dragTracked);
        Assert.True(result.dragPanned, "A drag starting on a tile must pan the property panel.");
        Assert.True(result.offsetAfterDrag > 0, "The drag must move the panel's vertical offset.");
        Assert.True(result.dragEndedAsPan);
    }

    [Fact]
    public async Task Styles_HaveNoComboBoxTouchBehaviorAndThePopupGlueIsGone()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var styles = Assert.IsType<ResourceDictionary>(
                Application.LoadComponent(new Uri("/TouchPanel;component/Theme/Styles.xaml", UriKind.Relative)));

            var comboStyleDeclared = styles.Contains(typeof(ComboBox));
            var touchSetters = new List<string>();
            foreach (DictionaryEntry entry in styles)
            {
                if (entry.Value is not Style style)
                {
                    continue;
                }

                foreach (var setterBase in style.Setters)
                {
                    if (setterBase is Setter { Property: { } property }
                        && (property.Name.Contains("TouchPan", StringComparison.Ordinal)
                            || property.OwnerType.FullName?.Contains("ComboBoxTouchScroll", StringComparison.Ordinal) == true))
                    {
                        touchSetters.Add($"{entry.Key}:{property.Name}");
                    }
                }
            }

            var assembly = typeof(EditOverlay).Assembly;
            var removedType = assembly.GetType("TouchPanel.Views.Controls.ComboBoxTouchScroll", throwOnError: false);

            // A combo using the app styles resolves no implicit style, so its dropdown template and
            // behavior are the stock WPF ones (the regression's hook is gone).
            var host = new StackPanel();
            host.Resources.MergedDictionaries.Add(styles);
            var combo = new ComboBox { ItemsSource = new[] { "one", "two" } };
            host.Children.Add(combo);
            host.Measure(new Size(300, 200));
            host.Arrange(new Rect(0, 0, 300, 200));
            host.UpdateLayout();

            return new
            {
                comboStyleDeclared,
                touchSetters = touchSetters.ToArray(),
                removedTypePresent = removedType is not null,
                comboStyle = combo.Style,
            };
        });

        Assert.False(result.comboStyleDeclared, "Styles.xaml must not declare an implicit ComboBox style.");
        Assert.Empty(result.touchSetters);
        Assert.False(result.removedTypePresent, "ComboBoxTouchScroll must be removed.");
        Assert.Null(result.comboStyle);
    }

    private static IconOption? Option(FrameworkElement element) => element.DataContext as IconOption;

    private static IEnumerable<IconOption> Options(InlineIconPicker picker) =>
        picker.ItemsSource?.Cast<IconOption>() ?? [];

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

    private static bool IsVisualDescendant(DependencyObject? node, DependencyObject ancestor)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static EditorContent CreateEditorContent(WidgetConfig widget)
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages =
            [
                new PageConfig { Id = "main", Widgets = [widget] },
            ],
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

        return new EditorContent(editor, page.Widgets[0], content, host);
    }

    private static BarWidgetConfig Bar() => new()
    {
        Id = "bar1",
        X = 0,
        Y = 0,
        Width = 240,
        Height = 64,
        Label = "bar1",
        Sensor = "cpu.total",
        Minimum = 0,
        Maximum = 100,
    };

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

    private sealed record EditorContent(
        PropertyEditorViewModel Editor,
        WidgetViewModelBase Widget,
        FrameworkElement Content,
        Grid Host);
}
