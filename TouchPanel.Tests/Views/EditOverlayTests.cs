using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Views;
using TouchPanel.Views.Controls;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Overlay wiring checks (T-12/T-13, RF-039/RF-043): the overlay binds the edit-mode service's
/// widget list and follows the edit-mode flag, so it is collapsed in normal mode and visible while
/// editing; the T-13 palette lists the built-in types and the Duplicate/Remove buttons track the
/// selection. The XAML is instantiated on an STA thread but never shown, so no desktop state is
/// touched; real touch drag/resize behavior is verified manually in T-18/MV-M2-05.
/// </summary>
public sealed class EditOverlayTests
{
    /// <summary>Generous upper bound for a menu transition to settle (the animations are 180–200 ms).</summary>
    private const int MenuTransitionTimeoutMilliseconds = 8000;

    [Fact]
    public async Task Overlay_BindsTheWidgetListAndFollowsEditModeVisibility()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());
            var service = new EditModeService(config, page, new RecordingConfigStore());
            overlay.DataContext = service;

            // Bindings attached before their data context existed activate on a queued dispatcher
            // operation; force it so the assertions observe the live bindings (in production the
            // Loaded event does the same).
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var root = Assert.IsType<Grid>(overlay.FindName("OverlayRoot"));
            var items = Assert.IsType<ItemsControl>(overlay.FindName("WidgetItems"));

            var itemsBound = items.Items.Count;
            var collapsedOutsideEditMode = root.Visibility;

            service.Enter(1024, 600);
            var visibleInEditMode = root.Visibility;

            // No dirty changes, so the exit completes synchronously through the no-op save path.
            service.ExitAsync().GetAwaiter().GetResult();
            var collapsedAfterExit = root.Visibility;

            return (itemsBound, collapsedOutsideEditMode, visibleInEditMode, collapsedAfterExit);
        });

        Assert.Equal(2, result.itemsBound);
        Assert.Equal(Visibility.Collapsed, result.collapsedOutsideEditMode);
        Assert.Equal(Visibility.Visible, result.visibleInEditMode);
        Assert.Equal(Visibility.Collapsed, result.collapsedAfterExit);
    }

    [Fact]
    public async Task Overlay_BindsThePaletteAndTracksSelectionInTheLifecycleButtons()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;

            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var palette = Assert.IsType<ItemsControl>(overlay.FindName("PaletteItems"));
            var widgetItems = Assert.IsType<ItemsControl>(overlay.FindName("WidgetItems"));
            var duplicate = Assert.IsType<Button>(overlay.FindName("DuplicateButton"));
            var remove = Assert.IsType<Button>(overlay.FindName("RemoveButton"));

            var paletteCount = palette.Items.Count;
            var disabledWithoutSelection = duplicate.IsEnabled;

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var enabledWithSelection = duplicate.IsEnabled;
            var removeEnabledWithSelection = remove.IsEnabled;

            // M2-RF-431/M2-RF-032: the added widget reaches the live host immediately.
            var beforeAdd = widgetItems.Items.Count;
            service.AddWidget(WidgetTypes.Button);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var afterAdd = widgetItems.Items.Count;

            return (paletteCount, disabledWithoutSelection, enabledWithSelection, removeEnabledWithSelection, beforeAdd, afterAdd);
        });

        Assert.Equal(10, result.paletteCount);
        Assert.False(result.disabledWithoutSelection);
        Assert.True(result.enabledWithSelection);
        Assert.True(result.removeEnabledWithSelection);
        Assert.Equal(2, result.beforeAdd);
        Assert.Equal(3, result.afterAdd);
    }

    [Fact]
    public async Task Overlay_ShowsTheAppsRowWithTenTouchSizedButtons()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var toolbar = Assert.IsType<Grid>(overlay.FindName("ToolbarGrid"));
            var label = Assert.IsType<TextBlock>(overlay.FindName("AppPresetLabel"));
            var apps = Assert.IsType<ItemsControl>(overlay.FindName("AppPresetItems"));
            var scroll = Assert.IsType<ScrollViewer>(overlay.FindName("AppPresetScroll"));

            var template = apps.ItemTemplate;
            var content = Assert.IsAssignableFrom<FrameworkElement>(template.LoadContent());
            var button = Assert.IsType<Button>(content);
            button.DataContext = apps.Items[0];

            var host = new Grid { Width = 1024, Height = 600 };
            host.Children.Add(content);
            host.Measure(new Size(1024, 600));
            host.Arrange(new Rect(0, 0, 1024, 600));
            host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var presenter = FindVisualDescendant<WidgetIconPresenter>(button);
            var text = FindVisualDescendant<TextBlock>(button, block => block.Text == "Steam");

            return (
                Rows: toolbar.RowDefinitions.Count,
                Label: label.Text,
                Count: apps.Items.Count,
                Panning: scroll.PanningMode,
                ButtonHeight: button.ActualHeight,
                MinHeight: button.MinHeight,
                Focusable: button.Focusable,
                HasIcon: presenter is not null,
                IconPath: presenter?.PathData,
                HasName: text is not null);
        });

        Assert.Equal(3, result.Rows);
        Assert.Equal("Apps", result.Label);
        Assert.Equal(10, result.Count);
        Assert.Equal(PanningMode.HorizontalOnly, result.Panning);
        Assert.True(result.ButtonHeight >= 40, $"App buttons must meet the 40 DIP touch target; was {result.ButtonHeight}.");
        Assert.True(result.MinHeight >= 40);
        Assert.False(result.Focusable, "App buttons must not pull keyboard focus.");
        Assert.True(result.HasIcon, "Each app button must render its brand icon.");
        Assert.False(string.IsNullOrWhiteSpace(result.IconPath));
        Assert.True(result.HasName, "Each app button must render its app name.");
    }

    private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        if (root is T match && (predicate is null || predicate(match)))
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            if (FindVisualDescendant(VisualTreeHelper.GetChild(root, index), predicate) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    [Fact]
    public async Task Overlay_ShowsTheTouchPropertyPanelForASelectedEditableWidget()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var collapsedWithoutSelection = panel.Visibility;

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            // UX fix #1: selection alone must not open the edit menu.
            var collapsedAfterSelection = panel.Visibility;

            // The per-widget Edit affordance opens the editor explicitly.
            service.OpenPropertyEditor();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var visibleAfterEdit = panel.Visibility;
            var panelContent = (panel.Child as ContentControl)?.Content;

            service.SelectWidget(null);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var collapsedAfterDeselect = panel.Visibility;

            // T-13/M2-RF-438: the field styles declare the 40 DIP touch-target minimum.
            var textBoxStyle = (Style)overlay.Resources["EditPropertyTextBoxStyle"];
            var checkBoxStyle = (Style)overlay.Resources["EditPropertyCheckBoxStyle"];
            var textBoxTouchSized = HasMinHeight(textBoxStyle, 40);
            var checkBoxTouchSized = HasMinHeight(checkBoxStyle, 40);

            return (
                collapsedWithoutSelection,
                collapsedAfterSelection,
                visibleAfterEdit,
                panelContent,
                collapsedAfterDeselect,
                textBoxTouchSized,
                checkBoxTouchSized);
        });

        Assert.Equal(Visibility.Collapsed, result.collapsedWithoutSelection);
        Assert.Equal(Visibility.Collapsed, result.collapsedAfterSelection);
        Assert.Equal(Visibility.Visible, result.visibleAfterEdit);
        Assert.IsType<TouchPanel.ViewModels.Edit.PropertyEditorViewModel>(result.panelContent);
        Assert.Equal(Visibility.Collapsed, result.collapsedAfterDeselect);
        Assert.True(result.textBoxTouchSized, "Property text inputs must meet the 40 DIP touch-target minimum.");
        Assert.True(result.checkBoxTouchSized, "Property toggle inputs must meet the 40 DIP touch-target minimum.");
    }

    [Fact]
    public async Task Overlay_ShowsTheActionPickerForAnActionBearingWidget()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithButton();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.OpenPropertyEditor();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var contentControl = Assert.IsType<ContentControl>(panel.Child);
            var content = (FrameworkElement)contentControl.ContentTemplate.LoadContent();
            content.DataContext = contentControl.Content;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            // The action type selector and the run-command browse button exist for a Button; the
            // browse affordance is visible only while a run-command action is selected.
            var actionTypeBox = content.FindName("ActionTypeBox") as ComboBox;
            var browseButton = content.FindName("ActionBrowseButton") as Button;
            var runCommandSection = content.FindName("ActionRunCommandSection") as StackPanel;

            var picker = service.PropertyEditor!.ActionPicker!;
            picker.SelectedActionOption = picker.ActionTypeOptions.First(option => option.Type == ActionTypes.RunCommand);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var browseVisibleForRunCommand = runCommandSection?.Visibility ?? Visibility.Collapsed;

            picker.SelectedActionOption = picker.ActionTypeOptions.First(option => option.Type == ActionTypes.Mute);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var browseVisibleForMute = runCommandSection?.Visibility ?? Visibility.Visible;

            return (actionTypeBox, browseButton, browseVisibleForRunCommand, browseVisibleForMute);
        });

        Assert.NotNull(result.actionTypeBox);
        Assert.Equal(7, result.actionTypeBox!.Items.Count);
        Assert.NotNull(result.browseButton);
        Assert.Equal(Visibility.Visible, result.browseVisibleForRunCommand);
        Assert.Equal(Visibility.Collapsed, result.browseVisibleForMute);
    }

    [Fact]
    public async Task Overlay_ShowsTheImageBrowseButtonForAnImageWidget()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithImage();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(
                config,
                page,
                new RecordingConfigStore(),
                factory: registry,
                palette: registry,
                imageFilePicker: new FakeImageFilePicker { Result = null });
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.OpenPropertyEditor();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var contentControl = Assert.IsType<ContentControl>(panel.Child);
            var content = (FrameworkElement)contentControl.ContentTemplate.LoadContent();
            content.DataContext = contentControl.Content;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var browseButton = content.FindName("ImageBrowseButton") as Button;
            var imagePathBox = content.FindName("ImagePathBox") as TextBox;

            return (browseButton is not null, browseButton?.IsEnabled ?? false, imagePathBox is not null);
        });

        Assert.True(result.Item1, "The image editor must expose a Browse button.");
        Assert.True(result.Item2, "The Browse button must be enabled when a picker seam is supplied.");
        Assert.True(result.Item3, "The image path TextBox must remain present.");
    }

    [Fact]
    public async Task Overlay_ShowsTheDisplayUnitSelectorForAGauge()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithGauge();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.OpenPropertyEditor();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var contentControl = Assert.IsType<ContentControl>(panel.Child);
            var content = (FrameworkElement)contentControl.ContentTemplate.LoadContent();
            content.DataContext = contentControl.Content;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var unitBox = content.FindName("DisplayUnitBox") as ComboBox;
            var sensorBox = content.FindName("SensorBox") as ComboBox;
            var iconPicker = content.FindName("IconPicker") as InlineIconPicker;
            var labelIconPicker = content.FindName("LabelIconPicker") as InlineIconPicker;
            var labelIconSection = content.FindName("LabelIconSection") as FrameworkElement;
            return (
                UnitBox: unitBox is not null,
                UnitCount: unitBox?.Items.Count ?? 0,
                SensorBox: sensorBox is not null,
                SensorCount: sensorBox?.Items.Count ?? 0,
                IconPicker: iconPicker is not null,
                IconCount: iconPicker?.ItemsSource?.Cast<object>().Count() ?? 0,
                LabelIconPicker: labelIconPicker is not null,
                LabelIconCount: labelIconPicker?.ItemsSource?.Cast<object>().Count() ?? 0,
                LabelIconVisibility: labelIconSection?.Visibility ?? Visibility.Collapsed);
        });

        Assert.True(result.UnitBox, "The Gauge editor must expose a display-unit selector.");
        Assert.Equal(6, result.UnitCount);

        // Only the two long icon catalogs switched to the inline picker; the shorter Sensor list
        // stays a ComboBox (its entries are dynamic and long, with no glyph preview).
        Assert.True(result.SensorBox, "The shorter Sensor list must stay a ComboBox.");
        Assert.True(result.SensorCount > 0);

        // User-requested addition: the Gauge renders a distinct label element, so the Label icon
        // picker is offered (None + built-in catalog). Both long icon lists use the inline picker
        // (collapsed) rather than a ComboBox.
        Assert.True(result.LabelIconPicker, "The Gauge editor must expose a label-icon picker.");
        Assert.True(result.LabelIconCount > 1, "The label-icon picker must offer None plus the catalog.");
        Assert.True(result.IconPicker, "The Gauge editor must expose the widget icon picker.");
        Assert.True(result.IconCount > 1, "The widget icon picker must offer the catalog.");
        Assert.Equal(Visibility.Visible, result.LabelIconVisibility);
    }

    [Fact]
    public async Task Overlay_HidesTheLabelIconSelectorForAWidgetWithoutADistinctLabel()
    {
        using var sta = new StaThread();

        var visibility = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.OpenPropertyEditor();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var contentControl = Assert.IsType<ContentControl>(panel.Child);
            var content = (FrameworkElement)contentControl.ContentTemplate.LoadContent();
            content.DataContext = contentControl.Content;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            // A Label widget shows its text as the widget itself; the label-icon option only applies
            // to the widgets with a distinct label element (Bar/Gauge/Sparkline/Slider).
            var section = Assert.IsType<StackPanel>(content.FindName("LabelIconSection"));
            return section.Visibility;
        });

        Assert.Equal(Visibility.Collapsed, visibility);
    }

    [Fact]
    public async Task Overlay_HostsTheGuideLayerAndSnapToggle()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var guideLayer = Assert.IsType<ItemsControl>(overlay.FindName("GuideLayer"));
            var snapToggle = Assert.IsType<ToggleButton>(overlay.FindName("SnapToggleButton"));
            var guidesBeforeEdit = guideLayer.Items.Count;

            service.Enter(1000, 500);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var snapCheckedOnEnter = snapToggle.IsChecked;

            // Move l1 (0,0,200,40) down by 47: grid-snaps to 48, then l2's top edge at 50 is within
            // the 4 DIP threshold, so l1 aligns to 50 and guides render.
            service.SelectWidget(page.Widgets[0]);
            service.BeginDrag(page.Widgets[0]);
            service.MoveWidgetBy(page.Widgets[0], 0, 47);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var guidesDuringDrag = guideLayer.Items.Count;
            var snappedY = page.Widgets[0].Y;

            service.EndDrag();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var guidesAfterDrag = guideLayer.Items.Count;

            return (guidesBeforeEdit, snapCheckedOnEnter, guidesDuringDrag, guidesAfterDrag, snappedY);
        });

        Assert.Equal(0, result.guidesBeforeEdit);
        Assert.True(result.snapCheckedOnEnter);
        Assert.True(result.guidesDuringDrag > 0);
        Assert.Equal(0, result.guidesAfterDrag);
        Assert.Equal(50, result.snappedY);
    }

    [Fact]
    public async Task Overlay_ShowsTheDeleteXAndTouchSizedConfirmation()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            // The "X" lives in each widget item template; load it to inspect its triggers (M5-RF-501).
            var items = Assert.IsType<ItemsControl>(overlay.FindName("WidgetItems"));
            var content = Assert.IsAssignableFrom<FrameworkElement>(items.ItemTemplate.LoadContent());
            var x = Assert.IsType<Button>(content.FindName("DeleteWidgetButton"));

            content.DataContext = page.Widgets[0];
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var collapsedWhenUnselected = x.Visibility;
            var xFocusable = x.Focusable;

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var visibleWhenSelected = x.Visibility;

            // The confirmation surface is an overlay-level element, collapsed until requested.
            var confirmation = Assert.IsType<Border>(overlay.FindName("DeleteConfirmation"));
            var collapsedWithoutRequest = confirmation.Visibility;

            service.RequestRemoveSelectedWidget();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var visibleAfterRequest = confirmation.Visibility;

            service.CancelRemove();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var collapsedAfterCancel = confirmation.Visibility;

            var confirmButton = overlay.FindName("ConfirmDeleteButton") as Button;
            var cancelButton = overlay.FindName("CancelDeleteButton") as Button;

            var deleteStyle = (Style)overlay.Resources["EditDeleteButtonStyle"];
            var toolbarStyle = (Style)overlay.Resources["EditToolbarButtonStyle"];

            return (
                collapsedWhenUnselected,
                visibleWhenSelected,
                xFocusable,
                collapsedWithoutRequest,
                visibleAfterRequest,
                collapsedAfterCancel,
                confirmButton,
                cancelButton,
                deleteStyleTouchSized: HasDoubleSetter(deleteStyle, FrameworkElement.MinWidthProperty, 40)
                    && HasDoubleSetter(deleteStyle, FrameworkElement.MinHeightProperty, 40),
                toolbarTouchSized: HasMinHeight(toolbarStyle, 40));
        });

        Assert.Equal(Visibility.Collapsed, result.collapsedWhenUnselected);
        Assert.Equal(Visibility.Visible, result.visibleWhenSelected);
        Assert.False(result.xFocusable, "The delete X must not pull keyboard focus.");
        Assert.Equal(Visibility.Collapsed, result.collapsedWithoutRequest);
        Assert.Equal(Visibility.Visible, result.visibleAfterRequest);
        Assert.Equal(Visibility.Collapsed, result.collapsedAfterCancel);
        Assert.NotNull(result.confirmButton);
        Assert.NotNull(result.cancelButton);
        Assert.True(result.deleteStyleTouchSized, "The delete X must meet the 40 DIP touch-target minimum.");
        Assert.True(result.toolbarTouchSized, "The confirmation buttons must meet the touch-target minimum.");
    }

    [Fact]
    public async Task Overlay_ShowsTheHotkeySectionForAHotkeyAction()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithHotkeyButton();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.OpenPropertyEditor();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var contentControl = Assert.IsType<ContentControl>(panel.Child);
            var content = (FrameworkElement)contentControl.ContentTemplate.LoadContent();
            content.DataContext = contentControl.Content;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var section = content.FindName("ActionHotkeySection") as StackPanel;
            var box = content.FindName("ActionHotkeyBox") as ComboBox;
            var emptyText = content.FindName("ActionHotkeyEmptyText") as TextBlock;

            var boxVisible = box?.Visibility ?? Visibility.Collapsed;
            var emptyVisible = emptyText?.Visibility ?? Visibility.Visible;
            var selected = (box?.SelectedItem as TouchPanel.ViewModels.Edit.HotkeyOption)?.Id;

            return (section, box, emptyVisible, boxVisible, selected);
        });

        Assert.NotNull(result.section);
        Assert.NotNull(result.box);
        Assert.Equal(2, result.box!.Items.Count);
        Assert.Equal(Visibility.Visible, result.boxVisible);
        Assert.Equal(Visibility.Collapsed, result.emptyVisible);
        Assert.Equal("h2", result.selected);
    }

    [Fact]
    public async Task Overlay_ShowsTheNoHotkeysStateForAButtonWithAHotkeyAction()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithButton();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.OpenPropertyEditor();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var contentControl = Assert.IsType<ContentControl>(panel.Child);
            var content = (FrameworkElement)contentControl.ContentTemplate.LoadContent();
            content.DataContext = contentControl.Content;

            var picker = service.PropertyEditor!.ActionPicker!;
            picker.SelectedActionOption = picker.ActionTypeOptions.First(option => option.Type == ActionTypes.Hotkey);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var box = content.FindName("ActionHotkeyBox") as ComboBox;
            var emptyText = content.FindName("ActionHotkeyEmptyText") as TextBlock;

            return (boxVisible: box?.Visibility ?? Visibility.Visible, emptyVisible: emptyText?.Visibility ?? Visibility.Collapsed);
        });

        Assert.Equal(Visibility.Collapsed, result.boxVisible);
        Assert.Equal(Visibility.Visible, result.emptyVisible);
    }

    [Fact]
    public async Task Overlay_ShowsATouchSizedEditButtonThatOpensTheEditorWithoutOverlappingTheXOrResize()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var items = Assert.IsType<ItemsControl>(overlay.FindName("WidgetItems"));
            var content = Assert.IsAssignableFrom<FrameworkElement>(items.ItemTemplate.LoadContent());
            var edit = Assert.IsType<Button>(content.FindName("EditWidgetButton"));
            var delete = Assert.IsType<Button>(content.FindName("DeleteWidgetButton"));
            var resize = Assert.IsType<Thumb>(content.FindName("ResizeWidgetThumb"));
            content.DataContext = page.Widgets[0];

            var host = new Grid { Width = 200, Height = 120 };
            host.Children.Add(content);
            host.Measure(new Size(200, 120));
            host.Arrange(new Rect(0, 0, 200, 120));
            host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var collapsedWhenUnselected = edit.Visibility;

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var visibleWhenSelected = edit.Visibility;
            var editBounds = BoundsWithin(edit, host);
            var deleteBounds = BoundsWithin(delete, host);
            var resizeBounds = BoundsWithin(resize, host);

            // UX fix #1: selection alone did not open the menu; pressing Edit opens it.
            var editorBeforeClick = service.HasPropertyEditor;
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var editorAfterClick = service.HasPropertyEditor;

            var editStyle = (Style)overlay.Resources["EditWidgetButtonStyle"];
            var deleteStyle = (Style)overlay.Resources["EditDeleteButtonStyle"];

            return (
                collapsedWhenUnselected,
                visibleWhenSelected,
                editBounds,
                deleteBounds,
                resizeBounds,
                editorBeforeClick,
                editorAfterClick,
                edit.Focusable,
                editTouchSized: HasDoubleSetter(editStyle, FrameworkElement.MinWidthProperty, 40)
                    && HasDoubleSetter(editStyle, FrameworkElement.MinHeightProperty, 40),
                deleteTouchSized: HasDoubleSetter(deleteStyle, FrameworkElement.MinWidthProperty, 40)
                    && HasDoubleSetter(deleteStyle, FrameworkElement.MinHeightProperty, 40),
                resizeTouchSized: resize.Width >= 40 && resize.Height >= 40);
        });

        Assert.Equal(Visibility.Collapsed, result.collapsedWhenUnselected);
        Assert.Equal(Visibility.Visible, result.visibleWhenSelected);
        Assert.False(result.Focusable, "The Edit button must not pull keyboard focus.");
        Assert.True(result.editTouchSized, "The Edit button must meet the 40 DIP touch-target minimum.");
        Assert.True(result.deleteTouchSized, "The delete X must meet the 40 DIP touch-target minimum.");
        Assert.True(result.resizeTouchSized, "The resize handle must meet the 40 DIP touch-target minimum.");

        Assert.False(result.editorBeforeClick, "Selecting a widget must not auto-open the edit menu.");
        Assert.True(result.editorAfterClick, "Pressing the Edit button must open that widget's edit menu.");

        Assert.False(Overlaps(result.editBounds, result.deleteBounds), $"Edit {result.editBounds} overlaps X {result.deleteBounds}.");
        Assert.False(Overlaps(result.editBounds, result.resizeBounds), $"Edit {result.editBounds} overlaps resize {result.resizeBounds}.");
        Assert.False(Overlaps(result.deleteBounds, result.resizeBounds), $"X {result.deleteBounds} overlaps resize {result.resizeBounds}.");
    }

    [Fact]
    public async Task Overlay_ResizeHandle_IsDiscoverableAndScopedToTheSelectedEditableWidget()
    {
        // UX fix #1: the bottom-right blue square is the resize handle. It must name itself (tooltip
        // + NWSE cursor) and only render for the selected, editable widget, so it never competes
        // with the Edit button or the delete X on unselected widgets.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var items = Assert.IsType<ItemsControl>(overlay.FindName("WidgetItems"));
            var content = Assert.IsAssignableFrom<FrameworkElement>(items.ItemTemplate.LoadContent());
            var resize = Assert.IsType<Thumb>(content.FindName("ResizeWidgetThumb"));
            content.DataContext = page.Widgets[0];

            var host = new Grid { Width = 200, Height = 120 };
            host.Children.Add(content);
            host.Measure(new Size(200, 120));
            host.Arrange(new Rect(0, 0, 200, 120));
            host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var collapsedWhenUnselected = resize.Visibility;
            var tooltip = resize.ToolTip as string;
            var cursor = resize.Cursor;
            var focusable = resize.Focusable;

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var visibleWhenSelected = resize.Visibility;

            service.SelectWidget(null);
            host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var collapsedAfterDeselect = resize.Visibility;

            return (collapsedWhenUnselected, visibleWhenSelected, collapsedAfterDeselect, tooltip, cursor, focusable);
        });

        Assert.Equal(Visibility.Collapsed, result.collapsedWhenUnselected);
        Assert.Equal(Visibility.Visible, result.visibleWhenSelected);
        Assert.Equal(Visibility.Collapsed, result.collapsedAfterDeselect);
        Assert.Equal("Drag to resize", result.tooltip);
        Assert.Equal(Cursors.SizeNWSE, result.cursor);
        Assert.False(result.focusable, "The resize handle must not pull keyboard focus.");
    }

    [Fact]
    public async Task Overlay_ExitEditModeButton_RequestsExitAndPersistsWhenDirty()
    {
        // UX fix #2: the toolbar's Exit Edit Mode button forwards the exit request; the composition
        // root (mirrored here) runs the same save-if-dirty path as Esc/tray.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var store = new RecordingConfigStore();
            var service = new EditModeService(config, page, store, factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var raised = 0;
            overlay.EditModeExitRequested += (_, _) =>
            {
                raised++;
                service.ExitAsync().GetAwaiter().GetResult();
            };

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.MoveWidgetBy(page.Widgets[0], 10, 0);
            var dirtyBefore = service.IsDirty;

            var button = Assert.IsType<Button>(overlay.FindName("ExitEditModeButton"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return (
                raised,
                dirtyBefore,
                isEditMode: service.IsEditMode,
                isDirty: service.IsDirty,
                saves: store.SaveCallCount,
                content: button.Content as string,
                focusable: button.Focusable);
        });

        Assert.Equal(1, result.raised);
        Assert.True(result.dirtyBefore);
        Assert.False(result.isEditMode);
        Assert.False(result.isDirty);
        Assert.Equal(1, result.saves);
        Assert.Equal("Exit Edit Mode", result.content);
        Assert.False(result.focusable, "The Exit Edit Mode button must not pull keyboard focus.");
    }

    [Fact]
    public async Task Overlay_PropertyPanel_HasRightPaddingForTheScrollBar()
    {
        // UX fix #3a: the property panel reserves right padding so the scrollbar has a gutter and
        // does not crowd the editor content.
        using var sta = new StaThread();

        var padding = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            return panel.Padding;
        });

        Assert.Equal(12, padding.Left);
        Assert.True(
            padding.Right >= 16,
            $"The property panel must reserve right padding for the scrollbar; was {padding.Right}.");
    }

    [Fact]
    public async Task Overlay_EditPanelsUseTheTouchPanBehaviorAndThePaletteStaysHorizontal()
    {
        // User-reported fix: in edit mode the property editor and the pages surface only scrolled by
        // dragging the scrollbar thumb, and a drag that started on a field did nothing. Both now use
        // the custom touch pan behavior (which consumes vertical drags over fields), and the built-in
        // PanningMode is no longer set on them so there is a single pan mechanism. The horizontal
        // palette widget strip is unchanged (PanningMode HorizontalOnly).
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var registry = WidgetRegistry.CreateDefault();
            var config = ConfigWithGauge();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.OpenPropertyEditor();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var contentControl = Assert.IsType<ContentControl>(panel.Child);
            var content = (FrameworkElement)contentControl.ContentTemplate.LoadContent();
            content.DataContext = contentControl.Content;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var propertyScroll = Assert.IsType<ScrollViewer>(content.FindName("PropertyPanelScroll"));
            var pagesScroll = Assert.IsType<ScrollViewer>(overlay.FindName("PagesPanelScroll"));
            var paletteScroll = Assert.IsType<ScrollViewer>(overlay.FindName("PaletteScroll"));

            return (
                Property: (TouchPan: TouchPan.GetIsEnabled(propertyScroll), Panning: propertyScroll.PanningMode, Horizontal: propertyScroll.HorizontalScrollBarVisibility, Vertical: propertyScroll.VerticalScrollBarVisibility, Focusable: propertyScroll.Focusable),
                Pages: (TouchPan: TouchPan.GetIsEnabled(pagesScroll), Panning: pagesScroll.PanningMode, Horizontal: pagesScroll.HorizontalScrollBarVisibility, Vertical: pagesScroll.VerticalScrollBarVisibility, Focusable: pagesScroll.Focusable),
                Palette: (Panning: paletteScroll.PanningMode, Horizontal: paletteScroll.HorizontalScrollBarVisibility, Vertical: paletteScroll.VerticalScrollBarVisibility),
                IsEditMode: service.IsEditMode);
        });

        Assert.True(result.IsEditMode, "The panning fix must not leave edit mode.");
        Assert.True(result.Property.TouchPan);
        Assert.Equal(PanningMode.None, result.Property.Panning);
        Assert.Equal(ScrollBarVisibility.Disabled, result.Property.Horizontal);
        Assert.Equal(ScrollBarVisibility.Auto, result.Property.Vertical);
        Assert.False(result.Property.Focusable, "Panning must not require keyboard focus.");

        Assert.True(result.Pages.TouchPan);
        Assert.Equal(PanningMode.None, result.Pages.Panning);
        Assert.Equal(ScrollBarVisibility.Disabled, result.Pages.Horizontal);
        Assert.Equal(ScrollBarVisibility.Auto, result.Pages.Vertical);
        Assert.False(result.Pages.Focusable, "Panning must not require keyboard focus.");

        // The palette is horizontal by design and must stay that way. Its bar is auto (visible only
        // when the strip overflows) because the row's buttons block the built-in PanningMode touch
        // panning; the bar is the discoverable way to reach the buttons that do not fit.
        Assert.Equal(PanningMode.HorizontalOnly, result.Palette.Panning);
        Assert.Equal(ScrollBarVisibility.Auto, result.Palette.Horizontal);
        Assert.Equal(ScrollBarVisibility.Disabled, result.Palette.Vertical);
    }

    [Fact]
    public async Task Overlay_PropertyCloseButton_ClosesTheEditorWithoutLeavingEditMode()
    {
        // UX fix #3b: the Close button at the end of the edit menu dismisses the editor only; edit
        // mode and the selection stay active.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.OpenPropertyEditor();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var contentControl = Assert.IsType<ContentControl>(panel.Child);
            var content = (FrameworkElement)contentControl.ContentTemplate.LoadContent();
            content.DataContext = contentControl.Content;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var close = Assert.IsType<Button>(content.FindName("PropertyCloseButton"));
            var editorBeforeClick = service.HasPropertyEditor;

            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return (
                editorBeforeClick,
                editorAfterClick: service.HasPropertyEditor,
                isEditMode: service.IsEditMode,
                hasSelection: service.SelectedWidget is not null,
                content: close.Content as string,
                close.Focusable);
        });

        Assert.True(result.editorBeforeClick);
        Assert.False(result.editorAfterClick);
        Assert.True(result.isEditMode, "Closing the edit menu must not leave edit mode.");
        Assert.True(result.hasSelection, "Closing the edit menu must keep the selection.");
        Assert.Equal("Close", result.content);
        Assert.True(result.Focusable, "The Close button must be focusable so it commits the active field before closing.");
    }

    [Fact]
    public async Task Overlay_MenuTab_IsAttachedCenteredAndSharesTheToolbarSheet()
    {
        // User feedback: the hide/show control must come out of the menu, not float above it. The
        // tab and the toolbar body are children of one sheet container; the tab is centered, sits
        // directly on the body (zero gap), and only rounds all four corners when the body is hidden.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var sheet = Assert.IsType<StackPanel>(overlay.FindName("EditSheet"));
            var toolbar = Assert.IsType<Border>(overlay.FindName("EditToolbar"));
            var toggle = Assert.IsType<Button>(overlay.FindName("ToggleEditMenuButton"));
            var propertyPanel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var pagesPanel = Assert.IsType<Border>(overlay.FindName("PagesPanel"));
            var pagesToggle = Assert.IsType<ToggleButton>(overlay.FindName("PagesToggleButton"));
            var actionCluster = Assert.IsType<WrapPanel>(overlay.FindName("ActionCluster"));
            var paletteScroll = Assert.IsType<ScrollViewer>(overlay.FindName("PaletteScroll"));
            var appScroll = Assert.IsType<ScrollViewer>(overlay.FindName("AppPresetScroll"));

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.OpenPropertyEditor();
            pagesToggle.IsChecked = true;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var sameContainer = ReferenceEquals(VisualTreeHelper.GetParent(toggle), sheet)
                && ReferenceEquals(VisualTreeHelper.GetParent(toolbar), sheet);
            var sheetChildren = sheet.Children.OfType<UIElement>().ToList();
            var visibleSurface = TemplateBorder(toggle, "TabSurface");

            var visibleState = (
                Content: toggle.Content as string,
                Visibility: toggle.Visibility,
                HitTest: toggle.IsHitTestVisible,
                Focusable: toggle.Focusable,
                Alignment: toggle.HorizontalAlignment,
                InsideToolbar: IsWithin(toggle, toolbar),
                InsideSheet: IsWithin(toggle, sheet),
                SameContainer: sameContainer,
                SheetRow: Grid.GetRow(sheet),
                MinWidth: toggle.MinWidth,
                MinHeight: toggle.MinHeight,
                TabCorners: visibleSurface.CornerRadius,
                TabBorderThickness: visibleSurface.BorderThickness,
                TabBackground: BrushColor(visibleSurface.Background),
                ToolbarBackground: BrushColor(toolbar.Background),
                ToolbarCorners: toolbar.CornerRadius,
                ToolbarBorderThickness: toolbar.BorderThickness,
                ChildCount: sheetChildren.Count,
                ToggleIsFirst: sheetChildren.FirstOrDefault() == toggle);

            var visibleLayout = new List<(
                string Label,
                bool NoGap,
                bool Centered,
                bool InsidePanel,
                bool Hittable,
                bool OverlapsProperty,
                bool OverlapsPages,
                bool ToolbarOverlapsProperty,
                bool ToolbarOverlapsPages,
                double Width,
                double Height,
                Rect ToggleBounds,
                Rect PropertyBounds,
                Rect PagesBounds,
                double SheetHeight,
                double ToolbarHeight,
                double ActionHeight,
                double PaletteHeight,
                double AppHeight)>();
            var hiddenLayout = new List<(
                string Label,
                Visibility ToolbarVisibility,
                bool ToolbarHitTest,
                Visibility ToggleVisibility,
                bool Hittable,
                bool Inside,
                bool OverlapsProperty,
                bool OverlapsPages,
                bool OnlyVisibleSheetChild,
                bool Centered,
                bool AtBottom,
                CornerRadius Corners,
                Thickness BorderThickness,
                string? Content)>();

            var host = new Grid();
            host.Children.Add(overlay);
            foreach (var (label, width, height) in new[] { ("1024x600", 1024d, 600d), ("512x300", 512d, 300d) })
            {
                host.Width = width;
                host.Height = height;
                host.Measure(new Size(width, height));
                host.Arrange(new Rect(0, 0, width, height));
                host.UpdateLayout();
                overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                var panelRect = new Rect(0, 0, width, height);
                var toggleBounds = BoundsWithin(toggle, overlay);
                var toolbarBounds = BoundsWithin(toolbar, overlay);
                var sheetBounds = BoundsWithin(sheet, overlay);
                var propertyBounds = BoundsWithin(propertyPanel, overlay);
                var pagesBounds = BoundsWithin(pagesPanel, overlay);

                visibleLayout.Add((
                    label,
                    Math.Abs(toggleBounds.Bottom - toolbarBounds.Top) <= 0.5,
                    Math.Abs((toggleBounds.Left + toggleBounds.Right) / 2 - (sheetBounds.Left + sheetBounds.Right) / 2) <= 0.5,
                    panelRect.Contains(toggleBounds) && panelRect.Contains(toolbarBounds),
                    IsHittable(overlay, toggle),
                    Overlaps(toggleBounds, propertyBounds),
                    Overlaps(toggleBounds, pagesBounds),
                    Overlaps(toolbarBounds, propertyBounds),
                    Overlaps(toolbarBounds, pagesBounds),
                    toggle.ActualWidth,
                    toggle.ActualHeight,
                    toggleBounds,
                    propertyBounds,
                    pagesBounds,
                    sheet.ActualHeight,
                    toolbar.ActualHeight,
                    actionCluster.ActualHeight,
                    paletteScroll.ActualHeight,
                    appScroll.ActualHeight));

                Assert.True(service.HideEditMenu());
                overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                PumpMenuTransition(host, () => toolbar.Visibility == Visibility.Collapsed);

                var hiddenBounds = BoundsWithin(toggle, overlay);
                var hiddenSurface = TemplateBorder(toggle, "TabSurface");
                hiddenLayout.Add((
                    label,
                    toolbar.Visibility,
                    toolbar.IsHitTestVisible,
                    toggle.Visibility,
                    IsHittable(overlay, toggle),
                    panelRect.Contains(hiddenBounds),
                    Overlaps(hiddenBounds, BoundsWithin(propertyPanel, overlay)),
                    Overlaps(hiddenBounds, BoundsWithin(pagesPanel, overlay)),
                    sheetChildren.Count(child => child.Visibility == Visibility.Visible) == 1,
                    Math.Abs((hiddenBounds.Left + hiddenBounds.Right) / 2 - width / 2) <= 0.5,
                    Math.Abs(hiddenBounds.Bottom - (height - 10)) <= 0.5,
                    hiddenSurface.CornerRadius,
                    hiddenSurface.BorderThickness,
                    toggle.Content as string));

                Assert.True(service.ShowEditMenu());
                overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                PumpMenuTransition(host, () => toolbar.Visibility == Visibility.Visible && !toolbar.HasAnimatedProperties);
            }

            var restored = (
                Toolbar: toolbar.Visibility,
                ToolbarHitTest: toolbar.IsHitTestVisible,
                Toggle: toggle.Visibility,
                Content: toggle.Content as string);

            return (visibleState, visibleLayout, hiddenLayout, restored);
        });

        Assert.Contains("Hide menu", result.visibleState.Content);
        Assert.Equal(Visibility.Visible, result.visibleState.Visibility);
        Assert.True(result.visibleState.HitTest);
        Assert.Equal(HorizontalAlignment.Center, result.visibleState.Alignment);
        Assert.False(result.visibleState.Focusable, "The menu tab must not pull keyboard focus.");
        Assert.True(result.visibleState.InsideSheet);
        Assert.False(result.visibleState.InsideToolbar, "The tab is the toolbar's attached sibling, not a child of its border.");
        Assert.True(result.visibleState.SameContainer, "The tab and the toolbar body must share one visual container (one sheet).");
        Assert.Equal(1, result.visibleState.SheetRow);
        Assert.Equal(2, result.visibleState.ChildCount);
        Assert.True(result.visibleState.ToggleIsFirst, "The tab must stack directly on top of the toolbar body.");
        Assert.True(result.visibleState.MinWidth >= 44 && result.visibleState.MinHeight >= 44, "The menu tab must meet the 44 DIP touch-target minimum.");
        Assert.Equal(new CornerRadius(10, 10, 0, 0), result.visibleState.TabCorners);
        Assert.Equal(new Thickness(1, 1, 1, 0), result.visibleState.TabBorderThickness);
        Assert.Equal(new CornerRadius(0, 0, 12, 12), result.visibleState.ToolbarCorners);
        Assert.Equal(new Thickness(1, 0, 1, 1), result.visibleState.ToolbarBorderThickness);
        Assert.Equal(Color.FromArgb(0xCC, 0x1E, 0x1E, 0x1E), result.visibleState.TabBackground);
        Assert.Equal(result.visibleState.TabBackground, result.visibleState.ToolbarBackground);

        Assert.Equal(2, result.visibleLayout.Count);
        foreach (var measurement in result.visibleLayout)
        {
            Assert.True(measurement.NoGap, $"[{measurement.Label}] The menu tab must sit directly on the toolbar with no vertical gap.");
            Assert.True(measurement.Centered, $"[{measurement.Label}] The menu tab must be horizontally centered on the sheet.");
            Assert.True(measurement.InsidePanel, $"[{measurement.Label}] The tab and toolbar must stay inside the panel.");
            Assert.True(measurement.Hittable, $"[{measurement.Label}] The menu tab must remain hit-testable.");

            // At 1024x600 the 300 DIP property panel sits clear of the centred tab. At 512x300 the
            // panel spans the whole panel width, so it necessarily covers the centred tab's column;
            // the panel is stacked above the sheet and owns input there, and the tab is restored as
            // soon as the editor closes. Only the roomy layout can require disjoint surfaces.
            if (measurement.Label == "1024x600")
            {
                Assert.False(measurement.OverlapsProperty, $"[{measurement.Label}] The menu tab {measurement.ToggleBounds} must not overlap the property panel {measurement.PropertyBounds}. sheet={measurement.SheetHeight} toolbar={measurement.ToolbarHeight} actions={measurement.ActionHeight} palette={measurement.PaletteHeight} apps={measurement.AppHeight}");
                Assert.False(measurement.OverlapsPages, $"[{measurement.Label}] The menu tab {measurement.ToggleBounds} must not overlap the Pages panel {measurement.PagesBounds}.");
            }
            Assert.False(measurement.ToolbarOverlapsProperty, $"[{measurement.Label}] The toolbar body must not overlap the property panel.");
            Assert.False(measurement.ToolbarOverlapsPages, $"[{measurement.Label}] The toolbar body must not overlap the Pages panel.");
            Assert.True(measurement.Width >= 44 && measurement.Height >= 44, $"[{measurement.Label}] The menu tab must be at least 44x44 DIP; was {measurement.Width}x{measurement.Height}.");
        }

        Assert.Equal(2, result.hiddenLayout.Count);
        foreach (var measurement in result.hiddenLayout)
        {
            Assert.Equal(Visibility.Collapsed, measurement.ToolbarVisibility);
            Assert.False(measurement.ToolbarHitTest, "The collapsed toolbar must not intercept touches.");
            Assert.Equal(Visibility.Visible, measurement.ToggleVisibility);
            Assert.True(measurement.Hittable, $"[{measurement.Label}] The menu tab must stay reachable by touch while the menu is hidden.");
            Assert.True(measurement.OnlyVisibleSheetChild, $"[{measurement.Label}] The menu tab must be the only visible control in the sheet while the menu is hidden.");
            Assert.True(measurement.Centered, $"[{measurement.Label}] The hidden menu tab must stay horizontally centered.");
            Assert.True(measurement.AtBottom, $"[{measurement.Label}] The hidden menu tab must stay at the bottom of the panel.");
            Assert.Contains("Edit menu", measurement.Content);
            Assert.True(measurement.Inside, $"[{measurement.Label}] The menu tab must stay inside the panel.");
            Assert.False(measurement.OverlapsProperty, $"[{measurement.Label}] The hidden menu tab must not overlap the property panel.");
            Assert.False(measurement.OverlapsPages, $"[{measurement.Label}] The hidden menu tab must not overlap the Pages panel.");
            Assert.Equal(new CornerRadius(10), measurement.Corners);
            Assert.Equal(new Thickness(1), measurement.BorderThickness);
        }

        Assert.Equal(Visibility.Visible, result.restored.Toolbar);
        Assert.True(result.restored.ToolbarHitTest);
        Assert.Equal(Visibility.Visible, result.restored.Toggle);
        Assert.Contains("Hide menu", result.restored.Content);
    }

    [Fact]
    public async Task Overlay_Toolbar_UsesTheNewSpacingAndSharedEightDipButtonGap()
    {
        // User feedback: more breathing room in the menu and evenly separated actions/palette/apps.
        // The shared button style owns the 8 DIP sibling gap (4 DIP per side), the toolbar body owns
        // the 12/8 padding, and both rows after the action cluster share an 8 DIP top margin.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            service.Enter(1024, 600);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var toolbar = Assert.IsType<Border>(overlay.FindName("EditToolbar"));
            var palette = Assert.IsType<ItemsControl>(overlay.FindName("PaletteItems"));
            var paletteScroll = Assert.IsType<ScrollViewer>(overlay.FindName("PaletteScroll"));
            var appRow = Assert.IsType<Grid>(overlay.FindName("AppPresetRow"));
            var apps = Assert.IsType<ItemsControl>(overlay.FindName("AppPresetItems"));
            var divider = Assert.IsType<Border>(overlay.FindName("ActionDivider"));

            var style = Assert.IsType<Style>(overlay.Resources["EditToolbarButtonStyle"]);
            var buttonMargin = GetSetterValue<Thickness>(style, FrameworkElement.MarginProperty);
            var paletteButton = Assert.IsType<Button>(palette.ItemTemplate.LoadContent());
            var appButton = Assert.IsType<Button>(apps.ItemTemplate.LoadContent());

            var actionButtons = new FrameworkElement[]
            {
                Assert.IsType<Button>(overlay.FindName("UndoButton")),
                Assert.IsType<Button>(overlay.FindName("RedoButton")),
                Assert.IsType<ToggleButton>(overlay.FindName("SnapToggleButton")),
                Assert.IsType<ToggleButton>(overlay.FindName("PagesToggleButton")),
                Assert.IsType<Button>(overlay.FindName("DuplicateButton")),
                Assert.IsType<Button>(overlay.FindName("RemoveButton")),
                Assert.IsType<Button>(overlay.FindName("ExitEditModeButton")),
            };

            return (
                Padding: toolbar.Padding,
                ButtonMargin: buttonMargin,
                ActionMargins: actionButtons.Select(button => button.Margin).ToArray(),
                PaletteIsShared: ReferenceEquals(paletteButton.Style, style),
                AppsIsShared: ReferenceEquals(appButton.Style, style),
                PaletteMargin: paletteButton.Margin,
                AppMargin: appButton.Margin,
                PaletteTop: paletteScroll.Margin.Top,
                AppsTop: appRow.Margin.Top,
                DividerMargin: divider.Margin,
                MinHeight: style.Setters.OfType<Setter>().Where(setter => setter.Property == FrameworkElement.MinHeightProperty).Select(setter => (double)setter.Value).FirstOrDefault());
        });

        Assert.Equal(new Thickness(12, 8, 12, 8), result.Padding);
        Assert.NotNull(result.ButtonMargin);
        Assert.Equal(new Thickness(4, 0, 4, 0), result.ButtonMargin!.Value);
        Assert.All(result.ActionMargins, margin => Assert.Equal(new Thickness(4, 0, 4, 0), margin));
        Assert.True(result.PaletteIsShared, "Palette buttons must use the shared toolbar button gap.");
        Assert.True(result.AppsIsShared, "App buttons must use the shared toolbar button gap.");
        Assert.Equal(new Thickness(4, 0, 4, 0), result.PaletteMargin);
        Assert.Equal(new Thickness(4, 0, 4, 0), result.AppMargin);
        Assert.Equal(8, result.PaletteTop);
        Assert.Equal(8, result.AppsTop);
        Assert.Equal(10, result.DividerMargin.Left);
        Assert.Equal(10, result.DividerMargin.Right);
        Assert.True(result.MinHeight >= 44, "The shared button gap must not shrink the touch target.");
    }

    private static Border TemplateBorder(Control control, string name)
    {
        control.ApplyTemplate();
        return Assert.IsType<Border>(control.Template.FindName(name, control));
    }

    private static Color BrushColor(Brush? brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    private static T? GetSetterValue<T>(Style style, DependencyProperty property)
        where T : struct
    {
        foreach (var setterBase in style.Setters)
        {
            if (setterBase is Setter { Property: var setProperty, Value: T value } && setProperty == property)
            {
                return value;
            }
        }

        return null;
    }

    [Fact]
    public async Task Overlay_MenuToggleHandlerFlipsTheToolbarStateBothWays()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var toolbar = Assert.IsType<Border>(overlay.FindName("EditToolbar"));
            var toggle = Assert.IsType<Button>(overlay.FindName("ToggleEditMenuButton"));

            var host = new Grid { Width = 1024, Height = 600 };
            host.Children.Add(overlay);
            host.Measure(new Size(1024, 600));
            host.Arrange(new Rect(0, 0, 1024, 600));
            host.UpdateLayout();

            service.Enter(1024, 600);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();

            var initiallyVisible = service.IsEditMenuVisible
                && toolbar.Visibility == Visibility.Visible
                && (toggle.Content as string)?.Contains("Hide menu") == true;

            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var collapsedImmediately = !service.IsEditMenuVisible;
            var hidingAnimates = toolbar.HasAnimatedProperties;
            PumpMenuTransition(host, () => toolbar.Visibility == Visibility.Collapsed);
            var hiddenAfterFirstClick = collapsedImmediately
                && toolbar.Visibility == Visibility.Collapsed
                && toggle.Visibility == Visibility.Visible
                && (toggle.Content as string)?.Contains("Edit menu") == true;

            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var showingAnimates = toolbar.HasAnimatedProperties;
            PumpMenuTransition(host, () => toolbar.Visibility == Visibility.Visible && !toolbar.HasAnimatedProperties);
            var visibleAfterSecondClick = service.IsEditMenuVisible
                && toolbar.Visibility == Visibility.Visible
                && toggle.Visibility == Visibility.Visible
                && (toggle.Content as string)?.Contains("Hide menu") == true;

            // Still a single edit session: Esc/shortcuts and drag/resize are unaffected by the toggle.
            var stillEditing = service.IsEditMode;

            return (initiallyVisible, hiddenAfterFirstClick, visibleAfterSecondClick, stillEditing, hidingAnimates, showingAnimates);
        });

        Assert.True(result.initiallyVisible, "The toggle must start in the visible (Hide menu) state.");
        Assert.True(result.hiddenAfterFirstClick, "The first click must hide the toolbar and relabel the toggle.");
        Assert.True(result.visibleAfterSecondClick, "The second click must restore the toolbar and relabel the toggle.");
        Assert.True(result.hidingAnimates, "Hiding must transition instead of snapping.");
        Assert.True(result.showingAnimates, "Showing must transition instead of snapping.");
        Assert.True(result.stillEditing);
    }

    [Fact]
    public async Task Overlay_MenuClose_AnimatesHeightAndOpacityThenCollapsesWithoutResidue()
    {
        // User-requested animation: hiding the edit menu must not snap. The body's height shrinks
        // from its laid-out height to zero while it fades out, the attached pull-tab rides down with
        // the shrinking sheet, and the completed state is the old collapsed state with no animated
        // height/opacity left behind.
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var host = new Grid { Width = 1024, Height = 600 };
            host.Children.Add(overlay);
            host.Measure(new Size(1024, 600));
            host.Arrange(new Rect(0, 0, 1024, 600));
            host.UpdateLayout();

            service.Enter(1024, 600);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();

            var toolbar = Assert.IsType<Border>(overlay.FindName("EditToolbar"));
            var tab = Assert.IsType<Button>(overlay.FindName("ToggleEditMenuButton"));
            var expandedHeight = toolbar.ActualHeight;
            var expandedTabBottom = BoundsWithin(tab, overlay).Bottom;

            Assert.True(service.HideEditMenu());

            var animatedImmediately = toolbar.HasAnimatedProperties;
            var visibilityWhileClosing = toolbar.Visibility;
            var hitTestWhileClosing = toolbar.IsHitTestVisible;
            var heightImmediately = toolbar.Height;
            var opacityImmediately = toolbar.Opacity;

            var samples = PumpMenuTransition(
                host,
                () => toolbar.Visibility == Visibility.Collapsed,
                () => (
                    toolbar.HasAnimatedProperties,
                    toolbar.Height,
                    toolbar.Opacity,
                    BoundsWithin(tab, overlay).Bottom));

            var tabBounds = BoundsWithin(tab, overlay);
            return new
            {
                ExpandedHeight = expandedHeight,
                ExpandedTabBottom = expandedTabBottom,
                AnimatedImmediately = animatedImmediately,
                VisibilityWhileClosing = visibilityWhileClosing,
                HitTestWhileClosing = hitTestWhileClosing,
                HeightImmediately = heightImmediately,
                OpacityImmediately = opacityImmediately,
                Samples = samples,
                FinalVisibility = toolbar.Visibility,
                FinalHeight = toolbar.Height,
                FinalOpacity = toolbar.Opacity,
                FinalAnimated = toolbar.HasAnimatedProperties,
                FinalHitTest = toolbar.IsHitTestVisible,
                TabVisibility = tab.Visibility,
                TabBounds = tabBounds,
            };
        });

        Assert.True(state.ExpandedHeight > 0, "The expanded toolbar must have a laid-out height before hiding.");
        Assert.True(state.AnimatedImmediately, "Hiding must start a height/opacity animation instead of snapping.");
        Assert.Equal(Visibility.Visible, state.VisibilityWhileClosing);
        Assert.False(state.HitTestWhileClosing, "A collapsing toolbar must stop intercepting touches immediately.");
        Assert.True(
            Math.Abs(state.HeightImmediately - state.ExpandedHeight) <= 0.5,
            $"The close animation must start at the expanded height {state.ExpandedHeight}; was {state.HeightImmediately}.");
        Assert.Equal(1, state.OpacityImmediately);

        // Mid-flight: both properties are between their endpoints, and the tab has moved down.
        Assert.Contains(state.Samples, sample => sample.Animated && sample.Height > 0 && sample.Height < state.ExpandedHeight - 0.5);
        Assert.Contains(state.Samples, sample => sample.Animated && sample.Opacity > 0 && sample.Opacity < 1);
        Assert.Contains(
            state.Samples,
            sample => sample.TabBottom > state.ExpandedTabBottom + 0.5 && sample.TabBottom < state.TabBounds.Bottom - 0.5);

        Assert.Equal(Visibility.Collapsed, state.FinalVisibility);
        Assert.True(double.IsNaN(state.FinalHeight), $"The animated height must be cleared to auto (NaN); was {state.FinalHeight}.");
        Assert.Equal(1, state.FinalOpacity);
        Assert.False(state.FinalAnimated, "No animated property may be left behind after the collapse.");
        Assert.False(state.FinalHitTest);
        Assert.Equal(Visibility.Visible, state.TabVisibility);
        Assert.Equal(512, (state.TabBounds.Left + state.TabBounds.Right) / 2, 1);
        Assert.Equal(590, state.TabBounds.Bottom, 1);
    }

    [Fact]
    public async Task Overlay_MenuOpen_AnimatesFromZeroToTheNaturalHeight()
    {
        // Opening grows the body from zero to its measured, width-constrained (wrapping) height and
        // fades it in; completion restores auto layout so nothing stays pinned.
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var host = new Grid { Width = 1024, Height = 600 };
            host.Children.Add(overlay);
            host.Measure(new Size(1024, 600));
            host.Arrange(new Rect(0, 0, 1024, 600));
            host.UpdateLayout();

            service.Enter(1024, 600);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();

            var toolbar = Assert.IsType<Border>(overlay.FindName("EditToolbar"));
            var naturalHeight = toolbar.ActualHeight;

            Assert.True(service.HideEditMenu());
            PumpMenuTransition(host, () => toolbar.Visibility == Visibility.Collapsed);

            Assert.True(service.ShowEditMenu());
            var animatedImmediately = toolbar.HasAnimatedProperties;
            var visibilityWhileOpening = toolbar.Visibility;
            var heightImmediately = toolbar.Height;
            var opacityImmediately = toolbar.Opacity;

            var samples = PumpMenuTransition(
                host,
                () => toolbar.Visibility == Visibility.Visible && !toolbar.HasAnimatedProperties,
                () => (toolbar.HasAnimatedProperties, toolbar.Height, toolbar.Opacity, 0d));

            host.UpdateLayout();
            return new
            {
                NaturalHeight = naturalHeight,
                AnimatedImmediately = animatedImmediately,
                VisibilityWhileOpening = visibilityWhileOpening,
                HeightImmediately = heightImmediately,
                OpacityImmediately = opacityImmediately,
                Samples = samples,
                FinalVisibility = toolbar.Visibility,
                FinalHeight = toolbar.Height,
                FinalOpacity = toolbar.Opacity,
                FinalAnimated = toolbar.HasAnimatedProperties,
                FinalActualHeight = toolbar.ActualHeight,
                FinalHitTest = toolbar.IsHitTestVisible,
            };
        });

        Assert.True(state.NaturalHeight > 0, "The expanded toolbar must have a laid-out height to grow into.");
        Assert.True(state.AnimatedImmediately, "Showing must start a height/opacity animation instead of appearing instantly.");
        Assert.Equal(Visibility.Visible, state.VisibilityWhileOpening);
        Assert.True(state.HeightImmediately <= 0.5, $"The open animation must start near zero height; was {state.HeightImmediately}.");
        Assert.Equal(0, state.OpacityImmediately);

        Assert.Contains(state.Samples, sample => sample.Animated && sample.Height > 0.5 && sample.Height < state.NaturalHeight - 0.5);
        Assert.Contains(state.Samples, sample => sample.Animated && sample.Opacity > 0 && sample.Opacity < 1);

        Assert.Equal(Visibility.Visible, state.FinalVisibility);
        Assert.True(double.IsNaN(state.FinalHeight), $"The animated height must be cleared to auto (NaN); was {state.FinalHeight}.");
        Assert.Equal(1, state.FinalOpacity);
        Assert.False(state.FinalAnimated, "No animated property may be left behind after the expansion.");
        Assert.True(state.FinalHitTest);
        Assert.True(state.FinalActualHeight > 0);
        Assert.Equal(state.NaturalHeight, state.FinalActualHeight, 1);
    }

    [Fact]
    public async Task Overlay_RapidMenuToggles_ConvergeToTheLastRequestedState()
    {
        // Rapid taps must never leave a pinned height/opacity: a new transition replaces the running
        // one and the final state always matches the last requested value.
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var host = new Grid { Width = 1024, Height = 600 };
            host.Children.Add(overlay);
            host.Measure(new Size(1024, 600));
            host.Arrange(new Rect(0, 0, 1024, 600));
            host.UpdateLayout();

            service.Enter(1024, 600);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();

            var toolbar = Assert.IsType<Border>(overlay.FindName("EditToolbar"));
            var naturalHeight = toolbar.ActualHeight;

            // Hide then show immediately: the close is overridden by the open.
            Assert.True(service.HideEditMenu());
            Assert.True(service.ShowEditMenu());
            PumpMenuTransition(host, () => toolbar.Visibility == Visibility.Visible && !toolbar.HasAnimatedProperties);
            host.UpdateLayout();
            var reopened = (
                toolbar.Visibility,
                toolbar.Height,
                toolbar.Opacity,
                toolbar.HasAnimatedProperties,
                toolbar.IsHitTestVisible,
                toolbar.ActualHeight);

            // Show then hide immediately: the open is overridden by the close.
            Assert.True(service.HideEditMenu());
            PumpMenuTransition(host, () => toolbar.Visibility == Visibility.Collapsed);
            Assert.True(service.ShowEditMenu());
            Assert.True(service.HideEditMenu());
            PumpMenuTransition(host, () => toolbar.Visibility == Visibility.Collapsed && !toolbar.HasAnimatedProperties);
            host.UpdateLayout();
            var rehidden = (
                toolbar.Visibility,
                toolbar.Height,
                toolbar.Opacity,
                toolbar.HasAnimatedProperties,
                toolbar.IsHitTestVisible);

            // A third tap while the replacement open animation is still running still lands hidden.
            Assert.True(service.ShowEditMenu());
            Assert.True(service.HideEditMenu());
            PumpMenuTransition(host, () => toolbar.Visibility == Visibility.Collapsed && !toolbar.HasAnimatedProperties);

            return new
            {
                NaturalHeight = naturalHeight,
                Reopened = reopened,
                Rehidden = rehidden,
                FinalVisibility = toolbar.Visibility,
                FinalHeight = toolbar.Height,
                FinalOpacity = toolbar.Opacity,
                FinalAnimated = toolbar.HasAnimatedProperties,
                FinalHitTest = toolbar.IsHitTestVisible,
            };
        });

        Assert.True(state.NaturalHeight > 0);
        Assert.Equal(Visibility.Visible, state.Reopened.Visibility);
        Assert.True(double.IsNaN(state.Reopened.Height));
        Assert.Equal(1, state.Reopened.Opacity);
        Assert.False(state.Reopened.HasAnimatedProperties);
        Assert.True(state.Reopened.IsHitTestVisible);
        Assert.Equal(state.NaturalHeight, state.Reopened.ActualHeight, 1);

        Assert.Equal(Visibility.Collapsed, state.Rehidden.Visibility);
        Assert.True(double.IsNaN(state.Rehidden.Height));
        Assert.Equal(1, state.Rehidden.Opacity);
        Assert.False(state.Rehidden.HasAnimatedProperties);
        Assert.False(state.Rehidden.IsHitTestVisible);

        Assert.Equal(Visibility.Collapsed, state.FinalVisibility);
        Assert.True(double.IsNaN(state.FinalHeight));
        Assert.Equal(1, state.FinalOpacity);
        Assert.False(state.FinalAnimated);
        Assert.False(state.FinalHitTest);
    }

    [Fact]
    public async Task Overlay_MenuStartsAndRebuildsWithoutAnimation()
    {
        // Construction, a page rebuild (new service instance), and session entry/exit all apply the
        // expanded state instantly; only a user toggle animates. Replacing the data context must also
        // detach the old service so a stale instance cannot drive this overlay.
        using var sta = new StaThread();

        var state = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var toolbar = Assert.IsType<Border>(overlay.FindName("EditToolbar"));
            var constructed = (
                toolbar.Visibility,
                toolbar.Height,
                toolbar.Opacity,
                toolbar.HasAnimatedProperties);

            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);

            var host = new Grid { Width = 1024, Height = 600 };
            host.Children.Add(overlay);
            host.Measure(new Size(1024, 600));
            host.Arrange(new Rect(0, 0, 1024, 600));
            host.UpdateLayout();

            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var afterContext = (toolbar.Visibility, toolbar.HasAnimatedProperties);

            service.Enter(1024, 600);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();
            var afterEnter = (toolbar.Visibility, toolbar.HasAnimatedProperties, toolbar.Height);

            // A page rebuild swaps in a fresh service with the menu expanded: no transition, and the
            // old instance stops driving the overlay. Set up the old instance as hidden first.
            var replacement = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            replacement.Enter(1024, 600);
            overlay.DataContext = replacement;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();
            Assert.True(replacement.HideEditMenu());
            PumpMenuTransition(host, () => toolbar.Visibility == Visibility.Collapsed);

            var rebuilt = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = rebuilt;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();
            var afterRebuild = (
                toolbar.Visibility,
                toolbar.Height,
                toolbar.Opacity,
                toolbar.HasAnimatedProperties);

            // The replaced service is still in edit mode and hidden; flipping it must not touch this
            // overlay (its handler was detached on the data-context change).
            Assert.True(replacement.ShowEditMenu());
            Assert.True(replacement.HideEditMenu());
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var afterStaleToggle = (toolbar.Visibility, toolbar.HasAnimatedProperties);

            // Exit/edit re-entry start expanded and instant: hide, exit (the service resets the menu
            // to visible), and re-enter without any animation.
            var current = (EditModeService)overlay.DataContext;
            current.Enter(1024, 600);
            Assert.True(current.HideEditMenu());
            PumpMenuTransition(host, () => toolbar.Visibility == Visibility.Collapsed);

            current.ExitAsync().GetAwaiter().GetResult();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();
            var afterExit = (
                toolbar.Visibility,
                toolbar.Height,
                toolbar.Opacity,
                toolbar.HasAnimatedProperties);

            current.Enter(1024, 600);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();
            var afterReenter = (toolbar.Visibility, toolbar.HasAnimatedProperties);

            return new
            {
                Constructed = constructed,
                AfterContext = afterContext,
                AfterEnter = afterEnter,
                AfterRebuild = afterRebuild,
                AfterStaleToggle = afterStaleToggle,
                AfterExit = afterExit,
                AfterReenter = afterReenter,
            };
        });

        Assert.Equal(Visibility.Visible, state.Constructed.Visibility);
        Assert.False(state.Constructed.HasAnimatedProperties, "Construction must not animate the menu.");
        Assert.Equal(1, state.Constructed.Opacity);

        Assert.Equal(Visibility.Visible, state.AfterContext.Visibility);
        Assert.False(state.AfterContext.HasAnimatedProperties, "Binding the initial service must not animate the menu.");

        Assert.Equal(Visibility.Visible, state.AfterEnter.Visibility);
        Assert.False(state.AfterEnter.HasAnimatedProperties, "Entering edit mode must not animate the menu.");
        Assert.True(double.IsNaN(state.AfterEnter.Height));

        Assert.Equal(Visibility.Visible, state.AfterRebuild.Visibility);
        Assert.True(double.IsNaN(state.AfterRebuild.Height));
        Assert.Equal(1, state.AfterRebuild.Opacity);
        Assert.False(state.AfterRebuild.HasAnimatedProperties, "A data-context replacement must apply the expanded state instantly.");

        Assert.Equal(Visibility.Visible, state.AfterStaleToggle.Visibility);
        Assert.False(state.AfterStaleToggle.HasAnimatedProperties, "The replaced service handler must be detached.");

        Assert.Equal(Visibility.Visible, state.AfterExit.Visibility);
        Assert.True(double.IsNaN(state.AfterExit.Height));
        Assert.Equal(1, state.AfterExit.Opacity);
        Assert.False(state.AfterExit.HasAnimatedProperties, "Exiting edit mode must show the menu instantly, not animate.");

        Assert.Equal(Visibility.Visible, state.AfterReenter.Visibility);
        Assert.False(state.AfterReenter.HasAnimatedProperties, "Re-entering edit mode must not animate the menu.");
    }

    [Fact]
    public async Task Overlay_KeepsActionsAboveThePaletteAndInsideThePanelAt1024x600()
    {
        // Layout regression (RF-023/RNF-015): the palette plus the action cluster are wider than the
        // 1024x600 panel at high DPI, so the toolbar uses a vertical two-row layout — the action
        // cluster (Snap / Duplicate / Remove / Exit Edit Mode) sits ABOVE the horizontally scrollable
        // palette. Measured with the property panel open (the toughest case), every action must render
        // inside the panel and be hit-testable, the toolbar must never exceed the panel, and the
        // palette must scroll rather than clip when it overflows. This fails on the old single-row
        // layout where the actions shared the palette's row and pushed Exit Edit Mode off the panel.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.OpenPropertyEditor();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var toolbar = Assert.IsType<Border>(overlay.FindName("EditToolbar"));
            var actionCluster = Assert.IsType<WrapPanel>(overlay.FindName("ActionCluster"));
            var exit = Assert.IsType<Button>(overlay.FindName("ExitEditModeButton"));
            var snap = Assert.IsType<ToggleButton>(overlay.FindName("SnapToggleButton"));
            var duplicate = Assert.IsType<Button>(overlay.FindName("DuplicateButton"));
            var remove = Assert.IsType<Button>(overlay.FindName("RemoveButton"));
            var propertyPanel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var paletteScroll = Assert.IsType<ScrollViewer>(overlay.FindName("PaletteScroll"));

            var measurements = new List<(
                string Label,
                double ExitWidth,
                bool ToolbarFits,
                bool ActionsAbovePalette,
                bool AllActionsFit,
                bool AllActionsHittable,
                bool PaletteFits,
                bool PaletteOverlapsProperty,
                bool ToolbarOverlapsProperty,
                bool PaletteNotClipped,
                double PaletteExtentWidth,
                double PaletteViewportWidth,
                double PaletteScrollableWidth,
                Rect ActionBounds,
                Rect PaletteBounds,
                Rect ToolbarBounds,
                Rect PropertyBounds)>();

            var host = new Grid();
            host.Children.Add(overlay);

            // 1024x600 is the target panel; 512x300 is the same panel at 200% scaling (WPF works in
            // DIPs, so scaling halves the logical layout size).
            foreach (var (label, width, height) in new[]
            {
                ("1024x600", 1024d, 600d),
                ("512x300", 512d, 300d),
            })
            {
                host.Width = width;
                host.Height = height;
                host.Measure(new Size(width, height));
                host.Arrange(new Rect(0, 0, width, height));
                host.UpdateLayout();
                overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                var panelRect = new Rect(0, 0, width, height);
                var toolbarBounds = BoundsWithin(toolbar, overlay);
                var actionBounds = BoundsWithin(actionCluster, overlay);
                var paletteBounds = BoundsWithin(paletteScroll, overlay);
                var propertyBounds = BoundsWithin(propertyPanel, overlay);

                var actionButtons = new FrameworkElement[] { snap, duplicate, remove, exit };
                var allActionsFit = actionButtons.All(button => panelRect.Contains(BoundsWithin(button, overlay)));
                var allActionsHittable = actionButtons.All(button => IsHittable(overlay, button));

                measurements.Add((
                    label,
                    exit.ActualWidth,
                    panelRect.Contains(toolbarBounds),
                    actionBounds.Bottom <= paletteBounds.Top + 0.5,
                    allActionsFit,
                    allActionsHittable,
                    panelRect.Contains(paletteBounds),
                    Overlaps(paletteBounds, propertyBounds),
                    Overlaps(toolbarBounds, propertyBounds),
                    // The palette is only required to scroll when its content exceeds the viewport;
                    // otherwise it must report a real viewport with nothing clipped.
                    paletteScroll.ViewportWidth > 0
                        && (paletteScroll.ScrollableWidth > 0 || paletteScroll.ExtentWidth <= paletteScroll.ViewportWidth + 0.5),
                    paletteScroll.ExtentWidth,
                    paletteScroll.ViewportWidth,
                    paletteScroll.ScrollableWidth,
                    actionBounds,
                    paletteBounds,
                    toolbarBounds,
                    propertyBounds));
            }

            return (measurements, propertyVisibility: propertyPanel.Visibility);
        });

        Assert.Equal(Visibility.Visible, result.propertyVisibility);

        foreach (var measurement in result.measurements)
        {
            Assert.True(measurement.ExitWidth >= 40, $"[{measurement.Label}] Exit button must meet the 40 DIP touch target; was {measurement.ExitWidth}.");
            Assert.True(measurement.ToolbarFits, $"[{measurement.Label}] Toolbar {measurement.ToolbarBounds} must fit inside the panel.");
            Assert.True(measurement.ActionsAbovePalette, $"[{measurement.Label}] Action cluster {measurement.ActionBounds} must sit above the palette {measurement.PaletteBounds}.");
            Assert.True(measurement.AllActionsFit, $"[{measurement.Label}] Snap/Duplicate/Remove/Exit must each fit inside the panel.");
            Assert.True(measurement.AllActionsHittable, $"[{measurement.Label}] Every action button must be hit-testable at its centre.");
            Assert.True(measurement.PaletteFits, $"[{measurement.Label}] Palette {measurement.PaletteBounds} must fit inside the panel.");
            Assert.False(measurement.PaletteOverlapsProperty, $"[{measurement.Label}] The palette {measurement.PaletteBounds} must not overlap the property panel.");
            Assert.False(measurement.ToolbarOverlapsProperty, $"[{measurement.Label}] The toolbar {measurement.ToolbarBounds} must not overlap the property panel.");
            Assert.True(
                measurement.PaletteNotClipped,
                $"[{measurement.Label}] The palette must scroll horizontally rather than clip (extent={measurement.PaletteExtentWidth}, viewport={measurement.PaletteViewportWidth}, scrollable={measurement.PaletteScrollableWidth}).");
        }

        // The 200%-scaled layout is narrower than the palette, so it must genuinely scroll horizontally
        // (the old single-row toolbar overflowed the panel here instead of scrolling within it).
        var narrow = result.measurements.Single(measurement => measurement.Label == "512x300");
        Assert.True(
            narrow.PaletteScrollableWidth > 0,
            $"[512x300] The palette must scroll horizontally when its content exceeds the panel width (extent={narrow.PaletteExtentWidth}, viewport={narrow.PaletteViewportWidth}).");
    }

    [Fact]
    public async Task Overlay_BothToolbarRowsStayInsideThePanelAndScrollToTheirLastItem()
    {
        // Regression (user-reported): the widget palette and the Apps strip were changed to a hidden
        // horizontal scrollbar. The row buttons handle touch, which blocks the built-in PanningMode
        // panning (see TouchPan), so with no bar the overflowed buttons were unreachable. The bar is
        // Auto again: it is only materialised when the strip overflows, and dragging it (or the
        // programmatic equivalent exercised here) reaches the last item at both panel sizes. The rows
        // must also keep a finite viewport so nothing is laid out or rendered outside the panel.
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithTwoWidgets();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            service.Enter(1024, 600);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var sheet = Assert.IsType<StackPanel>(overlay.FindName("EditSheet"));
            var toolbar = Assert.IsType<Border>(overlay.FindName("EditToolbar"));
            var tab = Assert.IsType<Button>(overlay.FindName("ToggleEditMenuButton"));
            var palette = Assert.IsType<ScrollViewer>(overlay.FindName("PaletteScroll"));
            var apps = Assert.IsType<ScrollViewer>(overlay.FindName("AppPresetScroll"));

            var rows = new List<(
                string Row,
                string Label,
                double PanelWidth,
                double Viewport,
                double Extent,
                double Scrollable,
                double OffsetAtEnd,
                ScrollBarVisibility Horizontal,
                ScrollBarVisibility Vertical,
                PanningMode Panning,
                Visibility ComputedBar,
                bool FitsPanel,
                bool Overflows,
                Rect LastBounds,
                bool LastFullyVisible,
                bool LastHittable,
                Rect ScrollBounds,
                Rect ToolbarBounds,
                Rect SheetBounds,
                Rect TabBounds,
                double TabCenterX)>();

            var host = new Grid();
            host.Children.Add(overlay);

            foreach (var (label, width, height) in new[] { ("1024x600", 1024d, 600d), ("512x300", 512d, 300d) })
            {
                host.Width = width;
                host.Height = height;
                host.Measure(new Size(width, height));
                host.Arrange(new Rect(0, 0, width, height));
                host.UpdateLayout();
                overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                var panelRect = new Rect(0, 0, width, height);
                var toolbarBounds = BoundsWithin(toolbar, overlay);
                var sheetBounds = BoundsWithin(sheet, overlay);
                var tabBounds = BoundsWithin(tab, overlay);

                foreach (var (row, scroll) in new[] { ("Palette", palette), ("Apps", apps) })
                {
                    scroll.ScrollToHorizontalOffset(0);
                    scroll.UpdateLayout();

                    var extent = scroll.ExtentWidth;
                    var viewport = scroll.ViewportWidth;
                    var scrollable = scroll.ScrollableWidth;
                    var computedBar = scroll.ComputedHorizontalScrollBarVisibility;

                    // Scroll exactly as far as the row allows, then check the final item is inside
                    // the viewport (i.e. the far edge is reachable, not merely present).
                    scroll.ScrollToHorizontalOffset(scrollable);
                    scroll.UpdateLayout();
                    overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                    var items = Assert.IsType<ItemsControl>(scroll.Content);
                    var lastContainer = Assert.IsAssignableFrom<FrameworkElement>(
                        items.ItemContainerGenerator.ContainerFromIndex(items.Items.Count - 1));
                    var lastBounds = BoundsWithin(lastContainer, scroll);

                    rows.Add((
                        row,
                        label,
                        width,
                        viewport,
                        extent,
                        scrollable,
                        scroll.HorizontalOffset,
                        scroll.HorizontalScrollBarVisibility,
                        scroll.VerticalScrollBarVisibility,
                        scroll.PanningMode,
                        computedBar,
                        BoundsWithin(scroll, overlay).Right <= width + 0.5
                            && BoundsWithin(scroll, overlay).Left >= -0.5,
                        extent > viewport + 0.5,
                        lastBounds,
                        lastBounds.Left >= -0.5 && lastBounds.Right <= viewport + 0.5,
                        IsHittable(overlay, lastContainer),
                        BoundsWithin(scroll, overlay),
                        toolbarBounds,
                        sheetBounds,
                        tabBounds,
                        (tabBounds.Left + tabBounds.Right) / 2));

                    scroll.ScrollToHorizontalOffset(0);
                    scroll.UpdateLayout();
                }
            }

            return rows;
        });

        Assert.Equal(4, result.Count);

        foreach (var row in result)
        {
            var tag = $"[{row.Label} {row.Row}]";
            Assert.Equal(ScrollBarVisibility.Auto, row.Horizontal);
            Assert.Equal(ScrollBarVisibility.Disabled, row.Vertical);
            Assert.Equal(PanningMode.HorizontalOnly, row.Panning);
            Assert.True(row.Viewport > 0, $"{tag} must report a real viewport.");
            Assert.True(
                row.Viewport <= row.PanelWidth,
                $"{tag} viewport {row.Viewport} must not exceed the {row.PanelWidth} DIP panel.");
            Assert.True(row.FitsPanel, $"{tag} row bounds {row.ScrollBounds} must stay inside the panel.");
            Assert.True(
                row.Extent > row.Viewport || row.Scrollable == 0,
                $"{tag} an overflowing row must scroll (extent={row.Extent}, viewport={row.Viewport}).");

            if (row.Overflows)
            {
                Assert.True(row.Scrollable > 0, $"{tag} must have a scrollable range.");
                Assert.Equal(Visibility.Visible, row.ComputedBar);
                Assert.True(
                    Math.Abs(row.OffsetAtEnd - row.Scrollable) <= 0.5,
                    $"{tag} must reach its full scrollable width (offset={row.OffsetAtEnd}, scrollable={row.Scrollable}).");
            }
            else
            {
                Assert.Equal(Visibility.Collapsed, row.ComputedBar);
            }

            Assert.True(
                row.LastFullyVisible,
                $"{tag} the last item {row.LastBounds} must be fully inside the viewport {row.Viewport} after scrolling.");
            Assert.True(row.LastHittable, $"{tag} the last item must be touch-reachable after scrolling.");

            // Nothing may render outside the panel: the sheet and its toolbar body keep their right
            // edge within the panel width at both sizes.
            Assert.True(row.SheetBounds.Right <= row.PanelWidth + 0.5, $"{tag} sheet {row.SheetBounds} must not exceed the panel width.");
            Assert.True(row.SheetBounds.Left >= -0.5, $"{tag} sheet {row.SheetBounds} must not start outside the panel.");
            Assert.True(row.ToolbarBounds.Right <= row.PanelWidth + 0.5, $"{tag} toolbar {row.ToolbarBounds} must not exceed the panel width.");

            // The attached pull-tab stays centred on the panel and touch-sized.
            Assert.True(
                Math.Abs(row.TabCenterX - row.PanelWidth / 2) <= 0.5,
                $"{tag} the pull-tab centre {row.TabCenterX} must match the panel centre {row.PanelWidth / 2}.");
            Assert.True(row.TabBounds.Width >= 44 && row.TabBounds.Height >= 44, $"{tag} the pull-tab must be at least 44x44 DIP.");
        }

        // At 512x300 (200% scaling) both strips are genuinely wider than their viewports, so both
        // must scroll, not clip. At 1024x600 the palette fits but the Apps strip does not.
        foreach (var row in result.Where(row => row.Label == "512x300"))
        {
            Assert.True(row.Overflows, $"[512x300 {row.Row}] must overflow and scroll (extent={row.Extent}, viewport={row.Viewport}).");
        }

        var appsAtFullSize = result.Single(row => row.Label == "1024x600" && row.Row == "Apps");
        Assert.True(
            appsAtFullSize.Overflows,
            $"[1024x600 Apps] must overflow and scroll (extent={appsAtFullSize.Extent}, viewport={appsAtFullSize.Viewport}).");
    }

    /// <summary>
    /// Pumps the real dispatcher (layout plus animation clocks) until the menu transition settles.
    /// The optional sampler records the live animated height/opacity and the pull-tab position on
    /// every tick; the guard timer only prevents a hang and is never used as a timing assertion.
    /// </summary>
    private static List<(bool Animated, double Height, double Opacity, double TabBottom)> PumpMenuTransition(
        Grid host,
        Func<bool> isSettled,
        Func<(bool Animated, double Height, double Opacity, double TabBottom)>? sample = null)
    {
        var observed = new List<(bool Animated, double Height, double Opacity, double TabBottom)>();
        var frame = new DispatcherFrame();
        var poll = new DispatcherTimer(
            TimeSpan.FromMilliseconds(10),
            DispatcherPriority.Render,
            (_, _) =>
            {
                host.UpdateLayout();
                if (sample is not null)
                {
                    observed.Add(sample());
                }

                if (isSettled())
                {
                    frame.Continue = false;
                }
            },
            Dispatcher.CurrentDispatcher);
        var guard = new DispatcherTimer(
            TimeSpan.FromMilliseconds(MenuTransitionTimeoutMilliseconds),
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);

        poll.Start();
        guard.Start();
        Dispatcher.PushFrame(frame);
        poll.Stop();
        guard.Stop();

        return observed;
    }

    private static Rect BoundsWithin(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static bool Overlaps(Rect a, Rect b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
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

    /// <summary>
    /// Verifies that the element's centre is the topmost hit under the overlay, proving it is
    /// reachable by touch rather than merely present in the visual tree.
    /// </summary>
    private static bool IsHittable(Visual root, FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        var center = element.TransformToAncestor(root)
            .Transform(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        var hit = VisualTreeHelper.HitTest(root, center)?.VisualHit;
        return hit is not null && IsWithin(hit, element);
    }

    private static PanelConfig ConfigWithHotkeyButton() => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = "main",
        Hotkeys =
        [
            new HotkeyConfig { Id = "h1", Modifiers = ["Ctrl"], Key = "A", Action = new MuteActionConfig() },
            new HotkeyConfig { Id = "h2", Modifiers = ["Alt"], Key = "B", Action = new MediaKeyActionConfig { Key = MediaKeyKind.Next } },
        ],
        Pages =
        [
            new PageConfig
            {
                Id = "main",
                Widgets =
                [
                    new ButtonWidgetConfig
                    {
                        Id = "b1",
                        X = 0,
                        Y = 0,
                        Width = 160,
                        Height = 120,
                        Action = new RunHotkeyActionConfig { HotkeyId = "h2" },
                        Label = "Hotkey",
                    },
                ],
            },
        ],
    };

    private static bool HasDoubleSetter(Style style, System.Windows.DependencyProperty property, double minimum)
    {
        foreach (var setterBase in style.Setters)
        {
            if (setterBase is Setter { Property: var setProperty, Value: { } value }
                && setProperty == property
                && value is double number
                && number >= minimum)
            {
                return true;
            }
        }

        return false;
    }

    private static PanelConfig ConfigWithButton() => new()
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
                    new ButtonWidgetConfig
                    {
                        Id = "b1",
                        X = 0,
                        Y = 0,
                        Width = 160,
                        Height = 120,
                        Action = new MuteActionConfig(),
                        Label = "Mute",
                    },
                ],
            },
        ],
    };

    private static PanelConfig ConfigWithImage() => new()
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
                    new ImageWidgetConfig
                    {
                        Id = "img1",
                        X = 0,
                        Y = 0,
                        Width = 240,
                        Height = 160,
                        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "touchpanel-image.png"),
                    },
                ],
            },
        ],
    };

    private static PanelConfig ConfigWithGauge() => new()
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
                ],
            },
        ],
    };

    private static bool HasMinHeight(Style style, double minimum)
    {
        foreach (var setterBase in style.Setters)
        {
            if (setterBase is Setter { Property: var property, Value: { } value }
                && property == FrameworkElement.MinHeightProperty
                && value is double height
                && height >= minimum)
            {
                return true;
            }
        }

        return false;
    }

    private static PanelConfig ConfigWithTwoWidgets() => new()
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
                    new LabelWidgetConfig { Id = "l1", X = 0, Y = 0, Width = 200, Height = 40, Label = "One" },
                    new LabelWidgetConfig { Id = "l2", X = 0, Y = 50, Width = 200, Height = 40, Label = "Two" },
                ],
            },
        ],
    };
}
