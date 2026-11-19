using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Paging;

namespace TouchPanel.Views;

/// <summary>
/// In-panel page selector and phone-homepage pager (T-16/RF-091, user-requested pages management).
/// The XAML owns the touch tab strip and the non-interactive dots/count indicator; this code-behind
/// owns only the pointer wiring for horizontal swipes and drags.
/// </summary>
/// <remarks>
/// <para>
/// Gestures are observed through preview events on the hosting window, so a recognized swipe can
/// mark the remaining move/up events handled before the widget under the finger executes its tap
/// action (and before a slider's thumb keeps following the drag). The threshold/direction policy
/// lives in the WPF-free <see cref="PageGestureTracker"/>; this class only feeds it points and
/// invokes the existing <see cref="PageSwitcherViewModel.MoveByAsync"/> paging path, which delegates
/// rebuild/persistence to the single-writer <c>PageSwitchCoordinator</c>.
/// </para>
/// <para>
/// Nothing here activates the window or changes focus, so normal mode stays non-activating. When
/// <see cref="IsPagingEnabled"/> reports <see langword="false"/> (edit mode, set by the composition
/// root) the tracker is canceled and the edit overlay keeps its drag/resize gestures untouched. Each
/// handler checks the gate before touching the event, so nothing is marked handled while paging is
/// disabled and the edit panels' touch panning (the property/pages <c>ScrollViewer</c>s) receives the
/// drag intact.
/// </para>
/// </remarks>
public partial class PageSwitcherView : UserControl
{
    private readonly PageGestureTracker _gesture = new();
    private Window? _window;
    private bool _suppressAction;

    /// <summary>Initializes a new instance of the <see cref="PageSwitcherView"/> class.</summary>
    public PageSwitcherView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Gets or sets the optional gate that disables paging gestures (the composition root returns
    /// <see langword="false"/> while edit mode is active). <see langword="null"/> means enabled.
    /// </summary>
    public Func<bool>? IsPagingEnabled { get; set; }

    /// <summary>
    /// Gets a value indicating whether a horizontal swipe is currently recognized. Exposed so
    /// window-level gesture tests can observe the decision without synthesizing device input.
    /// </summary>
    internal bool IsSwipeRecognized => _gesture.IsSwipeRecognized;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null || Window.GetWindow(this) is not { } window)
        {
            return;
        }

        _window = window;
        window.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        window.PreviewMouseMove += OnPreviewMouseMove;
        window.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        window.LostMouseCapture += OnLostMouseCapture;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not { } window)
        {
            return;
        }

        window.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        window.PreviewMouseMove -= OnPreviewMouseMove;
        window.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        window.LostMouseCapture -= OnLostMouseCapture;
        _window = null;
        CancelGesture();
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !PagingAllowed())
        {
            CancelGesture();
            return;
        }

        // Sliders, scrollbars, text fields and combo boxes own horizontal drag gestures. A swipe
        // starting on them must never page (or fight their own drag); a swipe starting anywhere
        // else is tracked and, once recognized, suppresses the widget's tap action.
        if (IsInteractiveGestureSource(e.OriginalSource as DependencyObject))
        {
            CancelGesture();
            return;
        }

        var point = e.GetPosition(_window);
        _gesture.Begin(point.X, point.Y);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_gesture.IsTracking)
        {
            return;
        }

        if (!PagingAllowed())
        {
            CancelGesture();
            return;
        }

        var point = e.GetPosition(_window);
        var outcome = _gesture.Move(point.X, point.Y);
        if (!outcome.Handled)
        {
            return;
        }

        // Consume the rest of the drag so the widget's button never sees the release and a slider's
        // thumb stops following the pointer. Releasing the widget's capture keeps it from continuing
        // its own gesture even if it already started tracking.
        e.Handled = true;
        if (!_suppressAction)
        {
            _suppressAction = true;
            if (Mouse.Captured is not null)
            {
                Mouse.Capture(null);
            }
        }
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_gesture.IsTracking)
        {
            return;
        }

        var point = e.GetPosition(_window);
        var outcome = _gesture.Complete(point.X, point.Y);
        var suppress = _suppressAction || outcome.Handled;

        if (suppress)
        {
            // The widget saw a press but must not see the matching release, so its tap action is
            // never executed for a swipe (requirement: swiping must not trigger a widget action).
            e.Handled = true;
        }

        if (outcome.Navigation != PageSwipeDirection.None && PagingAllowed())
        {
            Navigate(outcome.Navigation);
        }

        CancelGesture();
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        // Releasing a widget's capture is part of starting a recognized swipe, not a canceled
        // gesture: keep the tracker armed so the release still navigates exactly one page.
        if (!_suppressAction)
        {
            CancelGesture();
        }
    }

    private void CancelGesture()
    {
        _gesture.Cancel();
        _suppressAction = false;
    }

    private bool PagingAllowed() =>
        IsPagingEnabled?.Invoke() != false
        && DataContext is PageSwitcherViewModel { CanSwitch: true };

    private void Navigate(PageSwipeDirection direction)
    {
        if (DataContext is not PageSwitcherViewModel viewModel)
        {
            return;
        }

        var step = direction == PageSwipeDirection.Next ? 1 : -1;
        _ = MoveAsync(viewModel, step);
    }

    private static async Task MoveAsync(PageSwitcherViewModel viewModel, int step)
    {
        try
        {
            await viewModel.MoveByAsync(step).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // The view model and the coordinator already contain and log switch failures; a failed
            // swipe must never surface an exception to the dispatcher or strand the pager.
        }
    }

    /// <summary>
    /// Walks the source chain and reports whether the gesture started on a control that owns
    /// horizontal dragging (slider/scrollbar/thumb/text/combo), which must keep its own gesture.
    /// </summary>
    private static bool IsInteractiveGestureSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Slider or ScrollBar or Thumb or TextBoxBase or ComboBox)
            {
                return true;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : null;
        }

        return false;
    }

    // The hooks below are the deterministic seam used by the STA gesture tests. They call exactly
    // the same tracker/navigation methods as the real preview handlers, without synthesizing device
    // input or showing a window.

    internal bool BeginGestureForTest(double x, double y)
    {
        CancelGesture();
        return PagingAllowed() && _gesture.Begin(x, y);
    }

    internal PageGestureOutcome MoveGestureForTest(double x, double y)
    {
        var outcome = PagingAllowed() ? _gesture.Move(x, y) : PageGestureOutcome.Ignore;
        if (outcome.Handled)
        {
            _suppressAction = true;
        }

        return outcome;
    }

    internal PageGestureOutcome CompleteGestureForTest(double x, double y)
    {
        var outcome = PagingAllowed() ? _gesture.Complete(x, y) : PageGestureOutcome.Ignore;
        if (outcome.Navigation != PageSwipeDirection.None && PagingAllowed())
        {
            Navigate(outcome.Navigation);
        }

        _suppressAction = false;
        return outcome;
    }
}
