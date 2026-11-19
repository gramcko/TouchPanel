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
            return (unitBox is not null, unitBox?.Items.Count ?? 0);
        });

        Assert.True(result.Item1, "The Gauge editor must expose a display-unit selector.");
        Assert.Equal(5, result.Item2);
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

        // The palette is horizontal by design and must stay that way.
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
