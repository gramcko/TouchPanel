using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Views;

/// <summary>
/// Edit-mode overlay (T-12, RF-039/DD-7): one move <see cref="Thumb"/> and one corner resize
/// <see cref="Thumb"/> per widget, sharing the widget host's Canvas geometry. The code-behind is
/// pure overlay wiring — it maps presses and Thumb drag events to the bound
/// <see cref="EditModeService"/>, which owns selection, clamping, dirty tracking, and persistence.
/// WPF promotes touch to mouse for Thumb, so touch and mouse use the same path (no per-input
/// gesture engine).
/// </summary>
public partial class EditOverlay : UserControl
{
    /// <summary>Transition length for collapsing the edit menu (user-requested animation).</summary>
    private static readonly Duration MenuCloseDuration = new(TimeSpan.FromMilliseconds(180));

    /// <summary>Transition length for expanding the edit menu (user-requested animation).</summary>
    private static readonly Duration MenuOpenDuration = new(TimeSpan.FromMilliseconds(200));

    private EditModeService? _menuService;
    private double _expandedMenuHeight;
    private int _menuAnimationGeneration;
    private bool _wasInEditMode;

    /// <summary>Initializes a new instance of the <see cref="EditOverlay"/> class.</summary>
    public EditOverlay()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Raised on the dispatcher thread when the toolbar's <b>Exit Edit Mode</b> button is pressed.
    /// The composition root owns the exit (the same path as Esc/tray: save-if-dirty through the
    /// single-writer store, then restore non-activating normal mode and sync the tray state); the
    /// overlay only forwards the request and never activates the panel itself.
    /// </summary>
    public event EventHandler? EditModeExitRequested;

    /// <summary>Gets the edit-mode service bound as this overlay's data context, if any.</summary>
    private EditModeService? Service => DataContext as EditModeService;

    /// <summary>
    /// Binds the touch pages editor shown by the toolbar's <b>Pages…</b> toggle (user-requested page
    /// management inside edit mode). The composition root supplies a view model whose structural
    /// changes step out of and back into the edit session, so the overlay only hosts the surface.
    /// </summary>
    /// <param name="pagesEditor">The pages-management presentation state; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pagesEditor"/> is <see langword="null"/>.</exception>
    public void AttachPagesEditor(PagesEditorViewModel pagesEditor)
    {
        ArgumentNullException.ThrowIfNull(pagesEditor);

        PagesEditor.DataContext = pagesEditor;
    }

    /// <summary>
    /// Refreshes the pages list when the inline surface is opened, so pages added or renamed from the
    /// Settings window or an external edit are reflected. Refreshing is read-only and never enters
    /// an edit session or changes focus.
    /// </summary>
    /// <param name="sender">The Pages toggle.</param>
    /// <param name="e">The checked event data.</param>
    private void OnPagesToggleChecked(object sender, RoutedEventArgs e) =>
        (PagesEditor.DataContext as PagesEditorViewModel)?.Refresh();

    /// <summary>Closes the inline pages surface without leaving edit mode.</summary>
    /// <param name="sender">The Close pages button.</param>
    /// <param name="e">The click event data.</param>
    private void OnClosePagesClick(object sender, RoutedEventArgs e) =>
        PagesToggleButton.IsChecked = false;

    /// <summary>
    /// Selects the pressed widget (RF-027): a tap or the start of a drag gives arrow-key nudging a
    /// target, and the selection frame highlights it.
    /// </summary>
    /// <param name="sender">The item Grid whose data context is the widget view model.</param>
    /// <param name="e">The mouse button event data.</param>
    private void OnWidgetPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WidgetViewModelBase widget })
        {
            Service?.SelectWidget(widget);
        }
    }

    /// <summary>
    /// Begins a drag gesture (RF-044/M2-RF-444): selects the pressed widget and clears any stale
    /// guides so the guide layer only ever shows the current drag's alignments.
    /// </summary>
    /// <param name="sender">The move or resize <see cref="Thumb"/> whose drag started.</param>
    /// <param name="e">The drag-started event data.</param>
    private void OnThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is Thumb { DataContext: WidgetViewModelBase widget })
        {
            Service?.BeginDrag(widget);
        }
    }

    /// <summary>Ends a drag gesture (RF-044/M2-RF-444): clears the transient guides (never persisted).</summary>
    /// <param name="sender">The move or resize <see cref="Thumb"/> whose drag completed.</param>
    /// <param name="e">The drag-completed event data.</param>
    private void OnThumbDragCompleted(object sender, DragCompletedEventArgs e) => Service?.EndDrag();

    /// <summary>
    /// Moves the dragged widget by the drag delta with live edge clamping (RF-039) and grid/alignment
    /// snapping (RF-044/M2-RF-441/M2-RF-444). Holding Shift is the free-position override
    /// (M2-RF-445); touch uses the toolbar snap toggle instead.
    /// </summary>
    /// <param name="sender">The move <see cref="Thumb"/>.</param>
    /// <param name="e">Carries the horizontal and vertical change in DIPs.</param>
    private void OnMoveDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is Thumb { DataContext: WidgetViewModelBase widget })
        {
            Service?.MoveWidgetBy(widget, e.HorizontalChange, e.VerticalChange, IsFreePositionOverride);
        }
    }

    /// <summary>
    /// Resizes the dragged widget by the handle delta with live clamping, the D-9 minimum, and grid
    /// snapping (RF-044/M2-RF-441). Holding Shift is the free-position override (M2-RF-445).
    /// </summary>
    /// <param name="sender">The corner resize <see cref="Thumb"/>.</param>
    /// <param name="e">Carries the horizontal and vertical change in DIPs.</param>
    private void OnResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is Thumb { DataContext: WidgetViewModelBase widget })
        {
            Service?.ResizeWidgetBy(widget, e.HorizontalChange, e.VerticalChange, IsFreePositionOverride);
        }
    }

    /// <summary>Gets a value indicating whether the Shift free-position override is held (M2-RF-445).</summary>
    private static bool IsFreePositionOverride => (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

    /// <summary>Flips the edit-session grid snap from the touch toolbar toggle (M2-RF-441/M2-RF-445).</summary>
    /// <param name="sender">The Snap <see cref="ToggleButton"/>.</param>
    /// <param name="e">The click event data.</param>
    private void OnToggleSnapClick(object sender, RoutedEventArgs e) => Service?.ToggleSnap();

    /// <summary>
    /// Flips the edit-mode toolbar visibility from the attached pull-tab on top of it
    /// (user-requested hide/show edit menu). The tab is always visible in edit mode, sits directly on
    /// the toolbar sheet, and its label mirrors the state; the session-scoped state lives in the
    /// bound <see cref="EditModeService"/>.
    /// </summary>
    /// <param name="sender">The attached menu tab button.</param>
    /// <param name="e">The click event data.</param>
    private void OnToggleEditMenuClick(object sender, RoutedEventArgs e) => Service?.ToggleEditMenu();

    /// <summary>
    /// Re-binds the toolbar transition to the current service instance. A page rebuild replaces the
    /// service, so the previous handler is always detached first (no leaked subscriptions); the new
    /// instance's state is applied instantly, because a replacement already starts expanded.
    /// </summary>
    /// <param name="sender">The overlay.</param>
    /// <param name="e">The old and new data context.</param>
    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_menuService is not null)
        {
            _menuService.PropertyChanged -= OnMenuServicePropertyChanged;
        }

        _menuService = e.NewValue as EditModeService;

        if (_menuService is not null)
        {
            _menuService.PropertyChanged += OnMenuServicePropertyChanged;
        }

        _wasInEditMode = _menuService?.IsEditMode == true;
        ApplyMenuState(animate: false);
    }

    /// <summary>
    /// Follows the service's toolbar state. Only a user toggle during an active edit session
    /// animates; entering/leaving edit mode (which always resets the toolbar to expanded) applies
    /// the state immediately so a re-entry never flashes a transition.
    /// </summary>
    /// <param name="sender">The bound service.</param>
    /// <param name="e">The changed property.</param>
    private void OnMenuServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _menuService))
        {
            return;
        }

        if (e.PropertyName == nameof(EditModeService.IsEditMode))
        {
            _wasInEditMode = _menuService?.IsEditMode == true;
            ApplyMenuState(animate: false);
            return;
        }

        if (e.PropertyName != nameof(EditModeService.IsEditMenuVisible))
        {
            return;
        }

        ApplyMenuState(animate: _wasInEditMode && _menuService?.IsEditMode == true);
    }

    /// <summary>
    /// Applies the service's toolbar state to the body, either instantly (construction, data-context
    /// replacement, session entry/exit) or through the open/close transition (user toggle).
    /// </summary>
    /// <param name="animate">Whether to run the short height/opacity transition.</param>
    private void ApplyMenuState(bool animate)
    {
        var visible = _menuService?.IsEditMenuVisible ?? true;

        if (!animate)
        {
            StopMenuAnimation();
            EditToolbar.Height = double.NaN;
            EditToolbar.Opacity = 1;
            EditToolbar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            EditToolbar.IsHitTestVisible = visible;
            return;
        }

        if (visible)
        {
            AnimateMenuOpen();
        }
        else
        {
            AnimateMenuClose();
        }
    }

    /// <summary>
    /// Collapses the toolbar body: its height shrinks from the current laid-out height to zero while
    /// it fades out (ease-out cubic). The attached pull-tab is the body's stack sibling, so it rides
    /// to the bottom with the shrinking sheet. Completion collapses the body and clears the animated
    /// height so the element keeps its normal auto layout afterwards.
    /// </summary>
    private void AnimateMenuClose()
    {
        var toolbar = EditToolbar;
        var startHeight = CurrentMenuHeight();
        var startOpacity = toolbar.Opacity;
        var wasAnimating = toolbar.HasAnimatedProperties;

        // Remember the fully expanded height for the next open (unless we are only part way through
        // an earlier transition, whose intermediate height must not become the remembered one).
        if (!wasAnimating && startHeight > 0)
        {
            _expandedMenuHeight = startHeight;
        }

        StopMenuAnimation();
        var generation = _menuAnimationGeneration;

        // The body must stay visible while it shrinks; touching it is already off so a collapsing
        // toolbar cannot swallow taps meant for the page underneath.
        toolbar.Visibility = Visibility.Visible;
        toolbar.IsHitTestVisible = false;
        toolbar.Height = startHeight;
        toolbar.Opacity = startOpacity;

        var height = new DoubleAnimation
        {
            From = startHeight,
            To = 0,
            Duration = MenuCloseDuration,
            EasingFunction = CreateMenuEasing(),
        };
        height.Completed += (_, _) => CompleteMenuClose(generation);

        toolbar.BeginAnimation(FrameworkElement.HeightProperty, height, HandoffBehavior.SnapshotAndReplace);
        toolbar.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation
            {
                From = startOpacity,
                To = 0,
                Duration = MenuCloseDuration,
                EasingFunction = CreateMenuEasing(),
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Expands the toolbar body: it becomes visible at zero height, its natural (width-constrained)
    /// height is measured, and it grows/fades in over the transition. Completion clears the animated
    /// height and opacity so wrapping and layout behave exactly as before.
    /// </summary>
    private void AnimateMenuOpen()
    {
        var toolbar = EditToolbar;
        var wasCollapsed = toolbar.Visibility != Visibility.Visible;
        var fromHeight = CurrentMenuHeight();
        var fromOpacity = toolbar.Opacity;

        StopMenuAnimation();
        var generation = _menuAnimationGeneration;

        // Measure the natural height only after the body is measurable again; an animated height
        // would otherwise be the value Measure sees.
        toolbar.Visibility = Visibility.Visible;
        toolbar.IsHitTestVisible = true;
        toolbar.Height = double.NaN;
        toolbar.Opacity = wasCollapsed ? 0 : fromOpacity;

        var naturalHeight = MeasureMenuHeight();
        if (!(naturalHeight > 0))
        {
            // Nothing measurable to grow into (for example before the first layout pass): show the
            // body immediately rather than leaving it pinned at zero height.
            StopMenuAnimation();
            toolbar.Height = double.NaN;
            toolbar.Opacity = 1;
            return;
        }

        _expandedMenuHeight = naturalHeight;

        var startHeight = wasCollapsed ? 0 : Math.Min(fromHeight, naturalHeight);
        var startOpacity = wasCollapsed ? 0 : fromOpacity;
        toolbar.Height = startHeight;
        toolbar.Opacity = startOpacity;

        var height = new DoubleAnimation
        {
            From = startHeight,
            To = naturalHeight,
            Duration = MenuOpenDuration,
            EasingFunction = CreateMenuEasing(),
        };
        height.Completed += (_, _) => CompleteMenuOpen(generation, naturalHeight);

        toolbar.BeginAnimation(FrameworkElement.HeightProperty, height, HandoffBehavior.SnapshotAndReplace);
        toolbar.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation
            {
                From = startOpacity,
                To = 1,
                Duration = MenuOpenDuration,
                EasingFunction = CreateMenuEasing(),
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Gets the body's current visual height, whether it is animated or laid out.</summary>
    /// <returns>The height in DIPs, or zero when it has no measurable height.</returns>
    private double CurrentMenuHeight()
    {
        var height = EditToolbar.Height;
        if (!(height > 0) || double.IsInfinity(height))
        {
            height = EditToolbar.ActualHeight;
        }

        return double.IsFinite(height) && height > 0 ? height : 0;
    }

    /// <summary>
    /// Measures the body's natural height with the width it really gets from the sheet, so the open
    /// animation grows to the wrapped height instead of a stale or single-line value.
    /// </summary>
    /// <returns>The measured height in DIPs, falling back to the last known expanded height.</returns>
    private double MeasureMenuHeight()
    {
        var width = EditSheet.ActualWidth;
        if (!(width > 0) || double.IsInfinity(width))
        {
            width = EditToolbar.ActualWidth;
        }

        if (!(width > 0) || double.IsInfinity(width))
        {
            width = ActualWidth;
        }

        var constraint = width > 0 && double.IsFinite(width) ? width : double.PositiveInfinity;
        EditToolbar.Measure(new Size(constraint, double.PositiveInfinity));
        var measured = EditToolbar.DesiredSize.Height;
        return measured > 0 ? measured : _expandedMenuHeight;
    }

    /// <summary>Removes any running treatment and invalidates the previous completion callback.</summary>
    private void StopMenuAnimation()
    {
        _menuAnimationGeneration++;
        EditToolbar.BeginAnimation(FrameworkElement.HeightProperty, null);
        EditToolbar.BeginAnimation(UIElement.OpacityProperty, null);
    }

    /// <summary>
    /// Finishes the collapse unless a newer transition replaced it. Clearing the animated height
    /// (<see cref="double.NaN"/>) restores auto layout, and the body ends collapsed and untouchable.
    /// </summary>
    /// <param name="generation">The transition generation this completion belongs to.</param>
    private void CompleteMenuClose(int generation)
    {
        if (generation != _menuAnimationGeneration || _menuService?.IsEditMenuVisible == true)
        {
            return;
        }

        var toolbar = EditToolbar;
        toolbar.BeginAnimation(FrameworkElement.HeightProperty, null);
        toolbar.BeginAnimation(UIElement.OpacityProperty, null);
        toolbar.Height = double.NaN;
        toolbar.Opacity = 1;
        toolbar.Visibility = Visibility.Collapsed;
        toolbar.IsHitTestVisible = false;
    }

    /// <summary>
    /// Finishes the expansion unless a newer transition replaced it. Clearing the animated height and
    /// opacity leaves the body in its normal auto-laid-out state.
    /// </summary>
    /// <param name="generation">The transition generation this completion belongs to.</param>
    /// <param name="naturalHeight">The measured expanded height in DIPs.</param>
    private void CompleteMenuOpen(int generation, double naturalHeight)
    {
        if (generation != _menuAnimationGeneration || _menuService?.IsEditMenuVisible == false)
        {
            return;
        }

        var toolbar = EditToolbar;
        toolbar.BeginAnimation(FrameworkElement.HeightProperty, null);
        toolbar.BeginAnimation(UIElement.OpacityProperty, null);
        toolbar.Height = double.NaN;
        toolbar.Opacity = 1;
        toolbar.Visibility = Visibility.Visible;
        toolbar.IsHitTestVisible = true;
        _expandedMenuHeight = naturalHeight;
    }

    /// <summary>Creates the shared ease-out cubic curve used by both transitions.</summary>
    /// <returns>A new easing function instance (timelines must not share one).</returns>
    private static CubicEase CreateMenuEasing() => new() { EasingMode = EasingMode.EaseOut };

    /// <summary>
    /// Restores the previous edit-mode layout state (user-requested undo/redo) through the service,
    /// which owns the snapshot history, the page rebuild, and the dirty flag. The button is only
    /// enabled while an undo step exists.
    /// </summary>
    /// <param name="sender">The Undo toolbar button.</param>
    /// <param name="e">The click event data.</param>
    private void OnUndoClick(object sender, RoutedEventArgs e) => Service?.TryUndo();

    /// <summary>
    /// Re-applies an undone edit-mode layout state (user-requested undo/redo) through the service.
    /// The button is only enabled while a redo step exists.
    /// </summary>
    /// <param name="sender">The Redo toolbar button.</param>
    /// <param name="e">The click event data.</param>
    private void OnRedoClick(object sender, RoutedEventArgs e) => Service?.TryRedo();

    /// <summary>
    /// Adds a widget of the tapped palette type (RF-043/M2-RF-431). The palette item carries its
    /// config <c>type</c> in <see cref="FrameworkElement.Tag"/>; placement, id generation, and the
    /// live/selection/dirty updates are owned by the bound <see cref="EditModeService"/>.
    /// </summary>
    /// <param name="sender">The palette button whose tag is the widget type.</param>
    /// <param name="e">The click event data.</param>
    private void OnAddWidgetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string type })
        {
            Service?.AddWidget(type);
        }
    }

    /// <summary>
    /// Adds the tapped preset app (user-requested Apps row) as an ImageButton. The item's data
    /// context is the <see cref="TouchPanel.Config.AppPreset"/>; the service resolves the launch
    /// target, places the button with the same clamp/undo/save path as any added widget, and keeps
    /// edit mode active so several apps can be added in a row.
    /// </summary>
    /// <param name="sender">The app button whose data context is the preset.</param>
    /// <param name="e">The click event data.</param>
    private void OnAddAppClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TouchPanel.Config.AppPreset preset })
        {
            Service?.AddAppPreset(preset);
        }
    }

    /// <summary>Duplicates the selected widget (M2-RF-432) through the edit-mode service.</summary>
    /// <param name="sender">The Duplicate toolbar button.</param>
    /// <param name="e">The click event data.</param>
    private void OnDuplicateWidgetClick(object sender, RoutedEventArgs e) => Service?.DuplicateSelectedWidget();
    /// <summary>
    /// Opens the tapped widget's edit menu (UX fix #1): selecting a widget only enables move/resize
    /// and its affordances, so the property editor is opened explicitly here. The button is shown
    /// only for the selected, editable widget; selecting it defensively keeps the target
    /// deterministic. Reuses <see cref="EditModeService.OpenPropertyEditor"/>; it never executes the
    /// widget's bound action (edit mode owns taps, M2-RF-439).
    /// </summary>
    /// <param name="sender">The per-widget Edit button whose data context is the widget view model.</param>
    /// <param name="e">The click event data.</param>
    private void OnEditWidgetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WidgetViewModelBase widget })
        {
            Service?.SelectWidget(widget);
            Service?.OpenPropertyEditor();
        }
    }

    /// <summary>Removes the selected widget (M2-RF-432) through the edit-mode service.</summary>
    /// <param name="sender">The Remove toolbar button.</param>
    /// <param name="e">The click event data.</param>
    private void OnRemoveWidgetClick(object sender, RoutedEventArgs e) => Service?.RemoveSelectedWidget();

    /// <summary>
    /// Opens the "Are you sure?" confirmation for the tapped widget's "X" (M5-RF-501/M5-RF-502). The
    /// X is only visible for the selected, editable widget, so the tapped item is the current
    /// selection; selecting it defensively keeps the target deterministic. It never deletes directly
    /// and never fires the widget's bound action (edit mode owns taps, M2-RF-439).
    /// </summary>
    /// <param name="sender">The per-widget "X" button whose data context is the widget view model.</param>
    /// <param name="e">The click event data.</param>
    private void OnDeleteWidgetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WidgetViewModelBase widget })
        {
            Service?.SelectWidget(widget);
            Service?.RequestRemoveSelectedWidget();
        }
    }

    /// <summary>Confirms the open delete confirmation (M5-RF-502), removing the selected widget.</summary>
    /// <param name="sender">The Delete confirmation button.</param>
    /// <param name="e">The click event data.</param>
    private void OnConfirmDeleteClick(object sender, RoutedEventArgs e) => Service?.ConfirmRemove();

    /// <summary>Cancels the delete confirmation (M5-RF-502), keeping the widget and selection.</summary>
    /// <param name="sender">The Cancel confirmation button.</param>
    /// <param name="e">The click event data.</param>
    private void OnCancelDeleteClick(object sender, RoutedEventArgs e) => Service?.CancelRemove();

    /// <summary>
    /// Requests an edit-mode exit from the toolbar (UX fix #2). The overlay forwards the request so
    /// the composition root runs the same save-if-dirty + restore-non-activating path as Esc/tray.
    /// </summary>
    /// <param name="sender">The Exit Edit Mode toolbar button.</param>
    /// <param name="e">The click event data.</param>
    private void OnExitEditModeClick(object sender, RoutedEventArgs e) =>
        EditModeExitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Closes the open property editor without leaving edit mode (UX fix #3b). Closing is transient
    /// overlay state: the configuration, the dirty flag, and the selection are unchanged.
    /// </summary>
    /// <param name="sender">The Close button at the end of the edit menu.</param>
    /// <param name="e">The click event data.</param>
    private void OnClosePropertyEditorClick(object sender, RoutedEventArgs e) => Service?.ClosePropertyEditor();

}
