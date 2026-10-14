using System.Windows;
using System.Windows.Controls;
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
/// press-and-hold gesture (UX fix #1): a hold of ~500 ms requests edit mode and suppresses the tap,
/// while a shorter tap keeps its normal widget action. The decision logic lives in the WPF-free
/// <see cref="WidgetLongPressCoordinator"/>/<c>LongPressGate</c>; this class owns only the timer and
/// the event wiring.
/// </summary>
public partial class WidgetPageHost : UserControl
{
    private readonly WidgetLongPressCoordinator _longPress;
    private readonly DispatcherTimer _longPressTimer;

    /// <summary>Initializes a new instance of the <see cref="WidgetPageHost"/> class.</summary>
    public WidgetPageHost()
    {
        InitializeComponent();

        _longPress = new WidgetLongPressCoordinator(_ => EditModeRequested?.Invoke(this, EventArgs.Empty));
        _longPressTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
        {
            Interval = _longPress.Threshold,
        };
        _longPressTimer.Tick += OnLongPressTimerTick;
    }

    /// <summary>
    /// Raised on the dispatcher thread when a widget is long-pressed in normal mode. The composition
    /// root handles it through the same deliberate edit-mode entry path (which may activate, D-6); the
    /// host itself never activates the panel or executes an action.
    /// </summary>
    public event EventHandler? EditModeRequested;

    private void OnWidgetPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindWidget(e.OriginalSource as DependencyObject) is not { } widget)
        {
            return;
        }

        _longPress.Press(widget, DateTimeOffset.UtcNow);
        _longPressTimer.Stop();
        _longPressTimer.Interval = _longPress.Threshold;
        _longPressTimer.Start();
    }

    private void OnWidgetPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _longPressTimer.Stop();
        if (_longPress.Release())
        {
            // A long press already requested edit mode; stop the widget's tap action from firing by
            // handling the release before the template's button processes it.
            e.Handled = true;
        }
    }

    private void OnLongPressTimerTick(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        if (!_longPress.Elapsed(DateTimeOffset.UtcNow) && _longPress.IsPressed)
        {
            // The timer fired a hair before the threshold was reached; retry shortly while held.
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(25);
            _longPressTimer.Start();
        }
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
}
