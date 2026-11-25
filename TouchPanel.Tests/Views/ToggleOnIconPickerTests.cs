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
/// The user-requested "On icon (on state)" inline picker: offered only for Toggle widgets, beside the
/// existing icon picker in the Appearance section, listing "None" plus the whole catalog, applying
/// and clearing live (through the same undoable appearance path) and persisting on exit.
/// </summary>
public sealed class ToggleOnIconPickerTests
{
    [Fact]
    public async Task OnIconSection_IsShownForTogglesOnly()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var toggle = CreateEditor(Toggle());
            var gauge = CreateEditor(Gauge());

            var toggleSection = Assert.IsType<StackPanel>(toggle.Content.FindName("OnIconSection"));
            var gaugeSection = Assert.IsType<StackPanel>(gauge.Content.FindName("OnIconSection"));
            var togglePicker = Assert.IsType<InlineIconPicker>(toggle.Content.FindName("OnIconPicker"));
            var header = Assert.IsType<Button>(togglePicker.FindName("PickerHeader"));

            return new
            {
                ToggleVisibility = toggleSection.Visibility,
                GaugeVisibility = gaugeSection.Visibility,
                Options = togglePicker.ItemsSource?.Cast<IconOption>().Select(option => option.Id).ToArray() ?? [],
                HeaderHeight = header.ActualHeight,
                ToggleSupports = toggle.Editor.SupportsOnIcon,
                GaugeSupports = gauge.Editor.SupportsOnIcon,
            };
        });

        Assert.Equal(Visibility.Visible, result.ToggleVisibility);
        Assert.True(result.ToggleSupports, "The Toggle editor must offer the on-icon picker.");
        Assert.Equal(Visibility.Collapsed, result.GaugeVisibility);
        Assert.False(result.GaugeSupports, "Only the Toggle has an on state.");

        // "None" plus every catalog entry, including glyphs and the vector/line-art groups.
        Assert.Equal(IconCatalog.BuiltIn.Count + 1, result.Options.Length);
        Assert.Equal(string.Empty, result.Options[0]);
        Assert.Contains("star", result.Options);
        Assert.Contains("microphoneMuted", result.Options);
        Assert.Contains("steam", result.Options);
        Assert.True(result.HeaderHeight >= 40, $"The picker header must be at least 40 DIP; was {result.HeaderHeight}.");
    }

    [Fact]
    public async Task OnIconPicker_AppliesLiveAndNoneClearsIt()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Toggle(initialOn: true));
            var editor = context.Editor;
            var toggleViewModel = Assert.IsType<ToggleWidgetViewModel>(context.Widget);
            var picker = Assert.IsType<InlineIconPicker>(context.Content.FindName("OnIconPicker"));
            var header = Assert.IsType<Button>(picker.FindName("PickerHeader"));
            var tiles = Assert.IsType<ItemsControl>(picker.FindName("PickerTiles"));

            Assert.False(toggleViewModel.HasOnIcon);

            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            context.Host.UpdateLayout();

            var mutedTile = FindVisualDescendants<Button>(tiles)
                .Single(button => (button.DataContext as IconOption)?.Id == "microphoneMuted");
            mutedTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var applied = new
            {
                Config = ((ToggleWidgetConfig)editor.Config).OnIcon,
                Selected = editor.SelectedOnIcon.Id,
                Picker = picker.SelectedItem?.Id,
                toggleViewModel.HasOnIcon,
                toggleViewModel.UsesOnIcon,
                toggleViewModel.DisplayIconPathData,
                toggleViewModel.DisplayIconStroke,
                context.Service.CanUndo,
            };

            // Re-open and pick "None": the configured on icon clears and the base icon returns.
            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            var noneTile = FindVisualDescendants<Button>(tiles)
                .Single(button => (button.DataContext as IconOption)?.Id == string.Empty);
            noneTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            context.Host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return new
            {
                Applied = applied,
                Cleared = new
                {
                    Config = ((ToggleWidgetConfig)editor.Config).OnIcon,
                    Selected = editor.SelectedOnIcon.Id,
                    Picker = picker.SelectedItem?.Id,
                    toggleViewModel.HasOnIcon,
                    toggleViewModel.UsesOnIcon,
                    toggleViewModel.DisplayIconGlyph,
                    toggleViewModel.IconGlyph,
                },
            };
        });

        Assert.Equal("microphoneMuted", result.Applied.Config);
        Assert.Equal("microphoneMuted", result.Applied.Selected);
        Assert.Equal("microphoneMuted", result.Applied.Picker);
        Assert.True(result.Applied.HasOnIcon);
        Assert.True(result.Applied.UsesOnIcon);
        Assert.Equal(IconCatalog.PathDataFor("microphoneMuted"), result.Applied.DisplayIconPathData);
        Assert.True(result.Applied.DisplayIconStroke);
        Assert.True(result.Applied.CanUndo, "Selecting an on icon must be an undoable edit.");

        Assert.Null(result.Cleared.Config);
        Assert.Equal(string.Empty, result.Cleared.Selected);
        Assert.Equal(string.Empty, result.Cleared.Picker);
        Assert.False(result.Cleared.HasOnIcon);
        Assert.False(result.Cleared.UsesOnIcon);
        Assert.Equal(result.Cleared.IconGlyph, result.Cleared.DisplayIconGlyph);
    }

    [Fact]
    public async Task OnIconSelection_PersistsThroughTheSingleWriterOnExit()
    {
        using var sta = new StaThread();

        var saved = await sta.InvokeAsync(() =>
        {
            var context = CreateEditor(Toggle(initialOn: false));
            context.Editor.SelectedOnIcon = IconCatalog.BuiltIn.First(option => option.Id == "star");
            var result = context.Service.ExitAsync().GetAwaiter().GetResult();
            Assert.True(result.Succeeded);
            return (ToggleWidgetConfig)context.Store.LastSavedConfig!.Pages[0].Widgets[0];
        });

        Assert.Equal("star", saved.OnIcon);
    }

    private static ToggleWidgetConfig Toggle(bool initialOn = false) => new()
    {
        Id = "toggle-1",
        X = 0,
        Y = 0,
        Width = 160,
        Height = 60,
        Label = "Mic",
        Icon = "power",
        InitialOn = initialOn,
        Action = new MuteActionConfig(),
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

        return new EditorContent(editor, page.Widgets[0], content, host, service, store);
    }

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
        EditModeService Service,
        RecordingConfigStore Store);
}
