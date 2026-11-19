using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Touch-only drag-to-scroll behavior for vertical panels. <c>TouchPan.IsEnabled="True"</c> makes a
/// vertical touch drag that starts anywhere inside a <see cref="ScrollViewer"/> (including on a
/// <c>TextBox</c>, its text, a <c>ComboBox</c>, or a <c>CheckBox</c>) scroll the viewer, while a tap
/// still reaches the control normally.
/// </summary>
/// <remarks>
/// <para>
/// The viewer's built-in <c>PanningMode</c> panning is blocked by any child that handles touch, which
/// is why a drag that starts on a field did nothing. This behavior instead listens to the preview
/// touch events on the viewer (or on an <see cref="ItemsControl"/> whose template hosts the viewer,
/// such as a <c>ListView</c>/<c>ListBox</c>):
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Down</b>: record the contact and do not handle or capture, so the control under the finger
/// still receives the tap (focus, caret, dropdown, toggle).
/// </description></item>
/// <item><description>
/// <b>Move</b>: while the session is below its 8 DIP / vertical-dominant threshold nothing is
/// consumed. Once recognized, the viewer captures the touch and every move is marked handled and
/// applied with <see cref="ScrollViewer.ScrollToVerticalOffset"/>. Preview events tunnel from the
/// viewer down to the control, so handling them here runs before the field's own touch handling and
/// suppresses the touch-to-mouse promotion that would otherwise select text or move a caret.
/// </description></item>
/// <item><description>
/// <b>Up / lost capture</b>: a completed pan consumes the release (so no click fires) and ends the
/// session; a lost capture cancels it.
/// </description></item>
/// </list>
/// <para>
/// Touches that start on the viewer's own vertical scroll bar are ignored so its thumb/drag behavior
/// is untouched, and nothing is tracked when there is nothing to scroll. Mouse input is never
/// intercepted, so mouse selection inside a <c>TextBox</c> is unchanged.
/// </para>
/// </remarks>
public static class TouchPan
{
    /// <summary>
    /// Identifies the <c>TouchPan.IsEnabled</c> attached property. Set it on a
    /// <see cref="ScrollViewer"/> or on an <see cref="ItemsControl"/> that hosts one.
    /// </summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(TouchPan),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>
    /// Identifies the <c>TouchPan.IsPanExcluded</c> attached property. Set it on a subtree whose own
    /// drag gesture must win over panning (for example the page rows that drag to reorder); a touch
    /// that starts inside it never starts a pan.
    /// </summary>
    public static readonly DependencyProperty IsPanExcludedProperty = DependencyProperty.RegisterAttached(
        "IsPanExcluded",
        typeof(bool),
        typeof(TouchPan),
        new PropertyMetadata(false));

    private static readonly ConditionalWeakTable<DependencyObject, TouchPanBehavior> Behaviors = new();

    /// <summary>Sets whether the touch pan behavior is active on <paramref name="element"/>.</summary>
    /// <param name="element">The target element.</param>
    /// <param name="value">Whether to enable the behavior.</param>
    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets whether the touch pan behavior is active on <paramref name="element"/>.</summary>
    /// <param name="element">The target element.</param>
    /// <returns><see langword="true"/> when the behavior is attached.</returns>
    public static bool GetIsEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsEnabledProperty);
    }

    /// <summary>Marks a subtree whose own drag gesture must win over panning.</summary>
    /// <param name="element">The subtree root.</param>
    /// <param name="value">Whether panning is excluded for touches that start in the subtree.</param>
    public static void SetIsPanExcluded(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsPanExcludedProperty, value);
    }

    /// <summary>Gets whether panning is excluded for touches that start in the subtree.</summary>
    /// <param name="element">The subtree root.</param>
    /// <returns><see langword="true"/> when panning is excluded.</returns>
    public static bool GetIsPanExcluded(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsPanExcludedProperty);
    }

    /// <summary>
    /// Returns the behavior instance for a target. Used by the focused STA tests to drive the same
    /// session and scroll path as real touch without injecting hardware events.
    /// </summary>
    /// <param name="element">The element the behavior was enabled on.</param>
    /// <returns>The behavior, or <see langword="null"/> when it is not attached.</returns>
    internal static TouchPanBehavior? GetBehaviorForTest(DependencyObject element) =>
        Behaviors.TryGetValue(element, out var behavior) ? behavior : null;

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not UIElement)
        {
            return;
        }

        if (e.NewValue is true)
        {
            if (!Behaviors.TryGetValue(element, out var behavior))
            {
                behavior = new TouchPanBehavior(element);
                Behaviors.Add(element, behavior);
                behavior.Attach();
            }

            return;
        }

        if (Behaviors.TryGetValue(element, out var existing))
        {
            existing.Detach();
            Behaviors.Remove(element);
        }
    }
}

/// <summary>
/// One attached <see cref="TouchPan"/> behavior: preview touch wiring, the capture rule, and the
/// scroll application. Created per enabled element; not public surface.
/// </summary>
internal sealed class TouchPanBehavior
{
    private const long TestContactId = 1;

    private readonly DependencyObject _target;
    private readonly TouchPanSession _session = new();
    private ScrollViewer? _viewer;
    private TouchDevice? _device;
    private bool _captured;
    private bool _panning;

    internal TouchPanBehavior(DependencyObject target) => _target = target;

    /// <summary>Gets the scroll surface this behavior drives, when resolved.</summary>
    internal ScrollViewer? Viewer => _viewer;

    internal void Attach()
    {
        if (_target is not UIElement element)
        {
            return;
        }

        element.PreviewTouchDown += OnPreviewTouchDown;
        element.PreviewTouchMove += OnPreviewTouchMove;
        element.PreviewTouchUp += OnPreviewTouchUp;
        element.LostTouchCapture += OnLostTouchCapture;
        _viewer = ResolveViewer();
    }

    internal void Detach()
    {
        if (_target is not UIElement element)
        {
            return;
        }

        element.PreviewTouchDown -= OnPreviewTouchDown;
        element.PreviewTouchMove -= OnPreviewTouchMove;
        element.PreviewTouchUp -= OnPreviewTouchUp;
        element.LostTouchCapture -= OnLostTouchCapture;
        ReleaseCapture(force: true);
        _session.Cancel();
        _viewer = null;
    }

    /// <summary>Test seam: starts a contact using the same code path as a real touch down.</summary>
    /// <param name="x">Contact X in DIPs.</param>
    /// <param name="y">Contact Y in DIPs.</param>
    /// <param name="source">Optional hit-test source used for the scrollbar/exclusion checks.</param>
    /// <returns><see langword="true"/> when the contact is tracked.</returns>
    internal bool BeginForTest(double x, double y, DependencyObject? source = null) =>
        BeginCore(TestContactId, new Point(x, y), device: null, source);

    /// <summary>Test seam: feeds one move using the same code path as a real touch move.</summary>
    /// <param name="x">Contact X in DIPs.</param>
    /// <param name="y">Contact Y in DIPs.</param>
    /// <returns>The applied pan step.</returns>
    internal TouchPanUpdate MoveForTest(double x, double y) => MoveCore(TestContactId, new Point(x, y));

    /// <summary>Test seam: ends the contact using the same code path as a real touch up.</summary>
    /// <returns><see langword="true"/> when the contact had started panning.</returns>
    internal bool EndForTest() => EndCore(TestContactId);

    /// <summary>Test seam: cancels exactly as a lost touch capture does.</summary>
    internal void CancelForTest() => CancelCore();

    private void OnPreviewTouchDown(object? sender, TouchEventArgs e)
    {
        if (_session.ContactId == e.TouchDevice.Id)
        {
            // A fresh down for a contact the session still thinks is down means its up was missed
            // (for example the finger left the surface below the threshold). Reset instead of
            // getting stuck on that contact id.
            CancelCore();
        }

        var viewer = ResolveViewer();
        if (viewer is null)
        {
            return;
        }

        BeginCore(
            e.TouchDevice.Id,
            e.GetTouchPoint(viewer).Position,
            e.TouchDevice,
            e.OriginalSource as DependencyObject);
    }

    private void OnPreviewTouchMove(object? sender, TouchEventArgs e)
    {
        if (_device is null || _device.Id != e.TouchDevice.Id)
        {
            return;
        }

        var update = MoveCore(e.TouchDevice.Id, e.GetTouchPoint(_viewer!).Position);
        if (!update.IsPanning)
        {
            return;
        }

        if (!_panning)
        {
            _panning = true;
            _captured = _viewer!.CaptureTouch(e.TouchDevice);

            // The tap on the control under the finger already promoted to a mouse press that the
            // control captured. Now that the gesture became a pan, drop that capture so the control
            // (for example a Button) is not left pressed when no promoted mouse release is raised.
            ReleasePromotedMouse();
        }

        e.Handled = true;
    }

    private void OnPreviewTouchUp(object? sender, TouchEventArgs e)
    {
        var wasPanning = EndCore(e.TouchDevice.Id);
        if (wasPanning)
        {
            e.Handled = true;
        }
    }

    private void OnLostTouchCapture(object? sender, TouchEventArgs e)
    {
        if (_device is null || _device.Id != e.TouchDevice.Id)
        {
            return;
        }

        // LostTouchCapture bubbles. Grabbing the contact on the viewer makes the child that had it
        // (for example a TextBox) raise LostTouchCapture; that must not cancel the pan we just
        // started. Only losing the capture on our own scroll surface ends the gesture.
        if (!ReferenceEquals(e.OriginalSource, _viewer))
        {
            return;
        }

        CancelCore();
    }

    private bool BeginCore(long contactId, Point position, TouchDevice? device, DependencyObject? source)
    {
        var viewer = ResolveViewer();
        if (viewer is null)
        {
            return false;
        }

        if (!_session.IsTracking && !_session.IsMultiTouch)
        {
            if (viewer.ScrollableHeight <= 0)
            {
                return false;
            }

            if (source is not null
                && (IsVerticalScrollBar(source, viewer) || IsPanExcluded(source)))
            {
                return false;
            }
        }

        if (!_session.Begin(contactId, position.X, position.Y, viewer.VerticalOffset))
        {
            // A second contact (or hostile input) invalidates the gesture: drop any active pan.
            ReleaseCapture();
            return false;
        }

        _device = device;
        _captured = false;
        _panning = false;
        return true;
    }

    private TouchPanUpdate MoveCore(long contactId, Point position)
    {
        if (_viewer is null)
        {
            return TouchPanUpdate.None;
        }

        var update = _session.Move(contactId, position.X, position.Y, _viewer.ScrollableHeight);
        if (update.IsPanning)
        {
            _viewer.ScrollToVerticalOffset(update.Offset);
        }

        return update;
    }

    private bool EndCore(long contactId)
    {
        var wasPanning = _session.End(contactId);

        if (_device is not null && _device.Id == contactId)
        {
            ReleaseCapture();
        }

        return wasPanning;
    }

    private void CancelCore()
    {
        ReleaseCapture();
        _session.Cancel();
    }

    private void ReleaseCapture(bool force = false)
    {
        var device = _device;
        _device = null;
        _panning = false;

        if (!_captured && !force)
        {
            return;
        }

        _captured = false;
        if (device is not null && _viewer is not null)
        {
            _viewer.ReleaseTouchCapture(device);
        }
    }

    private void ReleasePromotedMouse()
    {
        if (_viewer is null || Mouse.Captured is not DependencyObject captured)
        {
            return;
        }

        if (IsWithin(captured, _viewer))
        {
            Mouse.Capture(null);
        }
    }

    private bool IsWithin(DependencyObject node, DependencyObject ancestor)
    {
        for (var current = node; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private ScrollViewer? ResolveViewer()
    {
        if (_viewer is not null)
        {
            return _viewer;
        }

        _viewer = _target as ScrollViewer ?? FindScrollViewer(_target);
        if (_viewer is not null && _viewer.PanningMode != PanningMode.None)
        {
            // A themed ItemsControl (ListView/ListBox) opts its template ScrollViewer into built-in
            // panning. The custom behavior is the single pan mechanism here, so disable it to avoid
            // two systems scrolling the same drag.
            _viewer.PanningMode = PanningMode.None;
        }

        return _viewer;
    }

    private bool IsPanExcluded(DependencyObject source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, _target); node = GetParent(node))
        {
            if (TouchPan.GetIsPanExcluded(node))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsVerticalScrollBar(DependencyObject source, ScrollViewer viewer)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, _target); node = GetParent(node))
        {
            if (node is ScrollBar { Orientation: Orientation.Vertical } bar
                && ReferenceEquals(bar.TemplatedParent, viewer))
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject node)
    {
        if (node is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(node);
        }

        return (node is FrameworkContentElement) ? LogicalTreeHelper.GetParent(node) : null;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        ScrollViewer? first = null;
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(node, index);
                if (child is ScrollViewer candidate)
                {
                    if (ReferenceEquals(candidate.TemplatedParent, root))
                    {
                        return candidate;
                    }

                    first ??= candidate;
                }

                pending.Enqueue(child);
            }
        }

        return first;
    }
}
