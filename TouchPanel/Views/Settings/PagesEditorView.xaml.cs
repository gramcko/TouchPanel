using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Views.Settings;

/// <summary>
/// Touch pages management surface (user-requested): a XAML view over a
/// <see cref="ViewModels.Settings.PagesEditorViewModel"/>. All add/rename/remove/open/reorder
/// behavior and persistence live in the view model; this class only wires pointer input to the
/// shared <see cref="PageReorderDragPolicy"/> and the view model's move operation.
/// </summary>
/// <remarks>
/// The same control is hosted by the Settings window and the edit-mode overlay, so both surfaces get
/// identical drag-to-reorder behavior. A press that travels far enough vertically becomes a drag;
/// releasing over a different slot persists the new order through the single writer. WPF promotes
/// touch input to mouse, so the same wiring serves mouse and touch. A drag beyond the threshold
/// handles the release event, so the row's own button never treats the gesture as a tap.
/// <para>
/// <b>Interplay with touch panning.</b> Both hosts wrap this view in a <c>ScrollViewer</c> with the
/// custom <c>TouchPan</c> drag-to-scroll behavior (user-reported fix: the panels only scrolled by
/// dragging the scrollbar thumb). The page list is marked <c>TouchPan.IsPanExcluded</c>, so a touch
/// that starts on a row is left entirely to the row's pointer handlers and reorder keeps priority
/// over the pane; a pan starts from the panel chrome/gaps and from any non-row content, including
/// over the fields. The always visible <c>Move up</c>/<c>Move down</c> buttons are the guaranteed
/// touch path for reordering and are not affected by panning. This coexistence is structural
/// (pointer capture + preview handlers), verified by the BAML/mouse-driven reorder tests; real touch
/// must be confirmed by hand.
/// </para>
/// </remarks>
public partial class PagesEditorView : UserControl
{
    private const double DraggedRowOpacity = 0.55;

    private Point _pressPosition;
    private FrameworkElement? _pressedRow;
    private int _pressedIndex = -1;
    private bool _isDragging;
    private FrameworkElement? _draggedRow;
    private int _draggedIndex = -1;

    /// <summary>Initializes a new instance of the <see cref="PagesEditorView"/> class.</summary>
    public PagesEditorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets a value indicating whether this surface exposes drag-to-reorder wiring. Both hosts share
    /// the control, so the flag is the same for the Settings pages editor and the edit-mode surface.
    /// </summary>
    public bool IsReorderDragEnabled => true;

    private void OnPageListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ResetPress();

        if (e.ChangedButton != MouseButton.Left || DataContext is not PagesEditorViewModel)
        {
            return;
        }

        var index = ResolveRowIndex(e.OriginalSource as DependencyObject);
        if (index < 0)
        {
            return;
        }

        _pressPosition = e.GetPosition(PageItems);
        _pressedIndex = index;
        _pressedRow = RowContainer(index);
    }

    private void OnPageListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedIndex < 0)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetPress();
            return;
        }

        var position = e.GetPosition(PageItems);
        if (!_isDragging)
        {
            var deltaX = position.X - _pressPosition.X;
            var deltaY = position.Y - _pressPosition.Y;
            if (!PageReorderDragPolicy.ShouldStartDrag(deltaX, deltaY))
            {
                return;
            }

            if (DataContext is not PagesEditorViewModel { CanManagePages: true })
            {
                ResetPress();
                return;
            }

            BeginDrag();
            if (!_isDragging)
            {
                return;
            }
        }

        e.Handled = true;
        var extents = CaptureRowExtents();
        var insertion = PageReorderDragPolicy.ResolveInsertionIndex(extents, position.Y);
        ShowDropIndicator(extents, insertion);
    }

    private async void OnPageListPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            ResetPress();
            return;
        }

        e.Handled = true;

        var extents = CaptureRowExtents();
        var position = e.GetPosition(PageItems);
        var insertion = PageReorderDragPolicy.ResolveInsertionIndex(extents, position.Y);
        var target = PageReorderDragPolicy.ResolveTargetIndex(
            _draggedIndex,
            insertion,
            PageItems.Items.Count);
        var pageId = DataContext is PagesEditorViewModel draggedViewModel && _draggedIndex >= 0
            ? draggedViewModel.Pages[_draggedIndex].Id
            : null;

        var sourceIndex = _draggedIndex;
        var viewModel = DataContext as PagesEditorViewModel;
        EndDragVisuals();

        if (viewModel is not null && pageId is not null && target >= 0 && target != sourceIndex)
        {
            try
            {
                await viewModel.MovePageAsync(pageId, target);
            }
            catch (Exception)
            {
                // The view model already turns failures into a status line and a log entry; this
                // guard only keeps the dispatcher alive.
            }
        }
    }

    private void OnPageListLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging && ReferenceEquals(e.OriginalSource, PageItems))
        {
            EndDragVisuals();
        }
    }

    private void BeginDrag()
    {
        // Take capture before touching the drag flags: the row button's capture loss is delivered
        // synchronously and must not be mistaken for the drag itself ending. When capture is not
        // available (some touch-promotion paths), the pointer events still tunnel through the list
        // while the finger is over it, so the drag continues either way.
        Mouse.Capture(PageItems, CaptureMode.SubTree);
        _isDragging = true;
        _draggedIndex = _pressedIndex;
        _draggedRow = _pressedRow;
        if (_draggedRow is not null)
        {
            _draggedRow.Opacity = DraggedRowOpacity;
        }
    }

    private void EndDragVisuals()
    {
        if (_draggedRow is not null)
        {
            _draggedRow.Opacity = 1d;
        }

        PageDropIndicator.Visibility = Visibility.Collapsed;

        var releaseCapture = Mouse.Captured == PageItems;
        _isDragging = false;
        _draggedRow = null;
        _draggedIndex = -1;
        ResetPress();

        if (releaseCapture)
        {
            Mouse.Capture(null);
        }
    }

    private void ResetPress()
    {
        _pressedIndex = -1;
        _pressedRow = null;
        _pressPosition = default;
    }

    private FrameworkElement? RowContainer(int index) =>
        index >= 0 && index < PageItems.Items.Count
            ? PageItems.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement
            : null;

    private int ResolveRowIndex(DependencyObject? source)
    {
        if (source is null)
        {
            return -1;
        }

        var container = ItemsControl.ContainerFromElement(PageItems, source) as FrameworkElement;
        return container is null ? -1 : PageItems.ItemContainerGenerator.IndexFromContainer(container);
    }

    private IReadOnlyList<PageRowExtent> CaptureRowExtents()
    {
        var count = PageItems.Items.Count;
        var extents = new List<PageRowExtent>(count);
        var fallbackTop = 0d;
        for (var index = 0; index < count; index++)
        {
            if (RowContainer(index) is { IsVisible: true, ActualHeight: > 0 } row)
            {
                var bounds = row.TransformToAncestor(PageItems)
                    .TransformBounds(new Rect(row.RenderSize));
                extents.Add(new PageRowExtent(bounds.Top, bounds.Height));
                fallbackTop = bounds.Bottom;
            }
            else
            {
                extents.Add(new PageRowExtent(fallbackTop, 0d));
            }
        }

        return extents;
    }

    private void ShowDropIndicator(IReadOnlyList<PageRowExtent> extents, int insertionIndex)
    {
        double y;
        if (extents.Count == 0)
        {
            y = 0d;
        }
        else if (insertionIndex <= 0)
        {
            y = extents[0].Top;
        }
        else if (insertionIndex >= extents.Count)
        {
            y = extents[^1].Top + extents[^1].Height;
        }
        else
        {
            y = extents[insertionIndex].Top;
        }

        PageDropIndicator.Margin = new Thickness(0, Math.Max(0d, y - 2d), 0, 0);
        PageDropIndicator.Visibility = Visibility.Visible;
    }
}
