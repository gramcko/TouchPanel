using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TouchPanel.ViewModels;

namespace TouchPanel.Views;

/// <summary>
/// Page host for the config-driven widgets (T-10, RF-032/DD-6). The view binds a
/// <c>PageViewModel</c> (assigned as the panel window's data context), positions each widget from its
/// view model geometry in DIPs through <c>Canvas</c> attached properties, and resolves each widget's
/// template through the <c>WidgetTemplateSelector</c>. The only code-behind is the normal-mode
/// press-and-hold gesture: a hold of ~500 ms on a widget or on empty panel space requests edit mode
/// and suppresses the tap, while a shorter tap keeps its normal action. A horizontal drag is a page
/// swipe and abandons the pending hold. The decision logic lives in the WPF-free
/// <see cref="WidgetLongPressCoordinator"/>/<see cref="EmptySpaceLongPressCoordinator"/>; this class
/// owns only the timer and the event wiring.
/// </summary>
public partial class WidgetPageHost : UserControl
{
    private readonly WidgetLongPressCoordinator _widgetLongPress;
    private readonly EmptySpaceLongPressCoordinator _emptySpaceLongPress;
    private readonly DispatcherTimer _longPressTimer;
    private PressKind _pressKind;
    private Point _pressPoint;
    private bool _hasPressPoint;

    /// <summary>Initializes a new instance of the <see cref="WidgetPageHost"/> class.</summary>
    public WidgetPageHost()
    {
        InitializeComponent();

        _widgetLongPress = new WidgetLongPressCoordinator(_ => RequestEditMode());
        _emptySpaceLongPress = new EmptySpaceLongPressCoordinator(RequestEditMode);
        _longPressTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
        {
            Interval = _widgetLongPress.Threshold,
        };
        _longPressTimer.Tick += OnLongPressTimerTick;

        // The pager's window-level gesture handler marks the release handled after a recognized
        // swipe. Listen with handledEventsToo so the long-press timer is still stopped and cannot
        // fire edit mode after the swipe (the widget's action remains suppressed by the pager).
        AddHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnWidgetPreviewMouseLeftButtonUp),
            handledEventsToo: true);

        // The same applies to moves once a swipe is recognized: observe them even when the pager has
        // already consumed the event, so a recognized horizontal drag always cancels the pending
        // long press before the threshold is reached.
        AddHandler(
            UIElement.PreviewMouseMoveEvent,
            new MouseEventHandler(OnWidgetPreviewMouseMove),
            handledEventsToo: true);
    }

    private enum PressKind
    {
        None,
        Widget,
        EmptySpace,
    }

    /// <summary>
    /// Raised on the dispatcher thread when a widget or empty panel space is long-pressed in normal
    /// mode. The composition root handles it through the same deliberate edit-mode entry path (which
    /// may activate, D-6); the host itself never activates the panel or executes an action.
    /// </summary>
    public event EventHandler? EditModeRequested;

    /// <summary>
    /// Gets the widget long-press coordinator driven by this host. Exposed to the deterministic view
    /// tests so the widget hold path stays covered without synthesizing device input.
    /// </summary>
    internal WidgetLongPressCoordinator WidgetLongPressForTest => _widgetLongPress;

    /// <summary>
    /// Gets the empty-space long-press coordinator driven by this host. Exposed to the deterministic
    /// view tests so the empty-space hold path stays covered without a real timer.
    /// </summary>
    internal EmptySpaceLongPressCoordinator EmptySpaceLongPressForTest => _emptySpaceLongPress;

    private void RequestEditMode() => EditModeRequested?.Invoke(this, EventArgs.Empty);

    private void OnWidgetPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (FindWidget(e.OriginalSource as DependencyObject) is { } widget)
        {
            BeginWidgetPress(widget, position);
            return;
        }

        // Empty panel space (the page background). Controls that own their own horizontal drag
        // must never double as an edit-mode affordance.
        if (IsInteractiveGestureSource(e.OriginalSource as DependencyObject))
        {
            CancelPress();
            return;
        }

        BeginEmptySpacePress(position);
    }

    private void BeginWidgetPress(WidgetViewModelBase widget, Point position)
    {
        _emptySpaceLongPress.Cancel();
        _pressKind = PressKind.Widget;
        _pressPoint = position;
        _hasPressPoint = true;
        _widgetLongPress.Press(widget, DateTimeOffset.UtcNow);
        RestartTimer(_widgetLongPress.Threshold);
    }

    private void BeginEmptySpacePress(Point position)
    {
        _widgetLongPress.Cancel();
        _pressKind = PressKind.EmptySpace;
        _pressPoint = position;
        _hasPressPoint = true;
        _emptySpaceLongPress.Press(DateTimeOffset.UtcNow);
        RestartTimer(_emptySpaceLongPress.Threshold);
    }

    private void RestartTimer(TimeSpan interval)
    {
        _longPressTimer.Stop();
        _longPressTimer.Interval = interval;
        _longPressTimer.Start();
    }

    private void OnWidgetPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_hasPressPoint || _pressKind == PressKind.None || !IsPressed)
        {
            return;
        }

        var deltaX = e.GetPosition(this).X - _pressPoint.X;
        var deltaY = e.GetPosition(this).Y - _pressPoint.Y;
        if (!EmptySpaceLongPressCoordinator.IsHorizontalSwipeAwayFromPress(deltaX, deltaY))
        {
            return;
        }

        // A predominantly horizontal drag is a page swipe (or an intentional horizontal gesture),
        // not a press-and-hold: abandon the pending edit-mode entry without suppressing the tap.
        _hasPressPoint = false;
        _longPressTimer.Stop();
        CancelPress();
    }

    /// <summary>
    /// Reports whether pointer movement from a press is a predominantly horizontal drag large enough
    /// to be a page swipe. Such a drag cancels the pending long press so the pager, not edit-mode
    /// entry, owns the gesture. Pure and testable without a window.
    /// </summary>
    /// <param name="pressPoint">The press origin in DIPs.</param>
    /// <param name="currentPoint">The current pointer position in DIPs.</param>
    /// <returns><see langword="true"/> when the press should be treated as a horizontal swipe.</returns>
    internal static bool IsHorizontalSwipeAwayFromPress(Point pressPoint, Point currentPoint)
    {
        var deltaX = currentPoint.X - pressPoint.X;
        var deltaY = currentPoint.Y - pressPoint.Y;
        return EmptySpaceLongPressCoordinator.IsHorizontalSwipeAwayFromPress(deltaX, deltaY);
    }

    private void OnWidgetPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _longPressTimer.Stop();
        _hasPressPoint = false;

        var suppress = _pressKind switch
        {
            PressKind.Widget => _widgetLongPress.Release(),
            PressKind.EmptySpace => _emptySpaceLongPress.Release(),
            _ => false,
        };

        _pressKind = PressKind.None;
        if (suppress)
        {
            // A long press already requested edit mode; stop the tap/click from firing.
            e.Handled = true;
        }
    }

    private void OnLongPressTimerTick(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();

        var fired = _pressKind switch
        {
            PressKind.Widget => _widgetLongPress.Elapsed(DateTimeOffset.UtcNow),
            PressKind.EmptySpace => _emptySpaceLongPress.Elapsed(DateTimeOffset.UtcNow),
            _ => false,
        };

        if (!fired && IsPressed && _pressKind != PressKind.None)
        {
            // The timer fired a hair before the threshold was reached; retry shortly while held.
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(25);
            _longPressTimer.Start();
        }
    }

    private bool IsPressed => _pressKind switch
    {
        PressKind.Widget => _widgetLongPress.IsPressed,
        PressKind.EmptySpace => _emptySpaceLongPress.IsPressed,
        _ => false,
    };

    private void CancelPress()
    {
        _hasPressPoint = false;
        _widgetLongPress.Cancel();
        _emptySpaceLongPress.Cancel();
        _pressKind = PressKind.None;
    }

    private static WidgetViewModelBase? FindWidget(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: WidgetViewModelBase widget })
            {
                return widget;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : null;
        }

        return null;
    }

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
}
