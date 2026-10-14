using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TouchPanel.ViewModels;

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
    /// <summary>Initializes a new instance of the <see cref="EditOverlay"/> class.</summary>
    public EditOverlay()
    {
        InitializeComponent();
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
