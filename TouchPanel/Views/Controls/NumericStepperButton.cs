using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Touch-first <b>−</b>/<b>+</b> stepper for a numeric property field. A tap applies exactly one step
/// immediately (the button's own click is suppressed, so a quick tap never steps twice); holding it
/// repeats after 400 ms and then every 80 ms through <see cref="NumericRepeatController"/>, and the
/// repeat stops on release, leave, capture loss, or reaching a bound.
/// </summary>
/// <remarks>
/// <para>The button is <see cref="UIElement.Focusable"/>-false and not a tab stop, so tapping it never
/// steals keyboard focus from the field or ends the field's live-edit session. It resolves the
/// current <see cref="INumericFieldHost"/> from its data context and asks the editor to step the
/// named field, which keeps the existing single-undo-group architecture.</para>
/// <para>Enablement is derived from the same step predicate the editor uses, so the button at a
/// bound is disabled (and a held repeat stops) without duplicating the range rules here.</para>
/// </remarks>
public sealed class NumericStepperButton : Button
{
    /// <summary>Identifies the numeric-field key property.</summary>
    public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(
        nameof(Field),
        typeof(string),
        typeof(NumericStepperButton),
        new PropertyMetadata(null, static (d, _) => ((NumericStepperButton)d).UpdateEnabled()));

    /// <summary>Identifies the <c>-1</c>/<c>+1</c> step direction property.</summary>
    public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(
        nameof(Direction),
        typeof(int),
        typeof(NumericStepperButton),
        new PropertyMetadata(-1));

    private readonly NumericRepeatController _repeat = new();
    private DispatcherTimer? _timer;
    private INumericFieldHost? _host;
    private bool _pressed;
    private bool _suppressClick;
    private bool _updatingEnabled;

    /// <summary>Initializes a new instance of the <see cref="NumericStepperButton"/> class.</summary>
    public NumericStepperButton()
    {
        Focusable = false;
        IsTabStop = false;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Width = 40;
        Height = 40;
        MinWidth = 40;
        MinHeight = 40;
        FontSize = 18;
        FontWeight = FontWeights.SemiBold;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        LayoutUpdated += OnLayoutUpdated;
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        LostMouseCapture += OnLostMouseCapture;
        MouseLeave += OnMouseLeave;
        PreviewTouchDown += OnPreviewTouchDown;
        PreviewTouchUp += OnPreviewTouchUp;
        LostTouchCapture += OnLostTouchCapture;
        TouchLeave += OnTouchLeave;
    }

    /// <summary>Gets or sets the numeric-field key this button steps (a property-editor VM name).</summary>
    public string? Field
    {
        get => (string?)GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    /// <summary>Gets or sets the step direction: <c>-1</c> for decrease, <c>+1</c> for increase.</summary>
    public int Direction
    {
        get => (int)GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnClick()
    {
        if (_suppressClick)
        {
            // The press already applied the first step; the release click must not apply it again.
            _suppressClick = false;
            return;
        }

        // Keyboard/automation invoke (or a click with no preceding press we handled) behaves as a
        // quick tap: one step, then end a stepper-opened group.
        if (_host is null)
        {
            AttachHost();
        }

        if (_host is not null && StepOnce())
        {
            _host.CommitNumericStepHold(Field ?? string.Empty);
        }

        base.OnClick();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachHost();

    // A button inside a data template can be realized after the inherited data context is in place,
    // so DataContextChanged may never fire for it. Re-evaluating enablement on layout is cheap and
    // keeps the initial bound state correct without a timer or a second binding.
    private void OnLayoutUpdated(object? sender, EventArgs e) => UpdateEnabled();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopPress(commit: false);
        DetachHost();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachHost();
        AttachHost();
    }

    private void AttachHost()
    {
        var host = DataContext as INumericFieldHost;
        if (ReferenceEquals(host, _host))
        {
            UpdateEnabled();
            return;
        }

        DetachHost();
        _host = host;
        if (_host is not null)
        {
            _host.PropertyChanged += OnHostPropertyChanged;
        }

        UpdateEnabled();
    }

    private void DetachHost()
    {
        if (_host is not null)
        {
            _host.PropertyChanged -= OnHostPropertyChanged;
            _host = null;
        }
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateEnabled();

    private void UpdateEnabled()
    {
        if (_updatingEnabled)
        {
            return;
        }

        _updatingEnabled = true;
        try
        {
            var field = Field;
            var shouldEnable = _host is not null
                && !string.IsNullOrEmpty(field)
                && _host.CanStepNumericField(field, Direction);
            if (IsEnabled != shouldEnable)
            {
                IsEnabled = shouldEnable;
            }
        }
        finally
        {
            _updatingEnabled = false;
        }
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartPress();

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressed)
        {
            StopPress(commit: true);
        }
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e) => StopPress(commit: true);

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_pressed)
        {
            StopPress(commit: true);
        }
    }

    private void OnPreviewTouchDown(object? sender, TouchEventArgs e) => StartPress();

    private void OnPreviewTouchUp(object? sender, TouchEventArgs e)
    {
        if (_pressed)
        {
            StopPress(commit: true);
        }
    }

    private void OnLostTouchCapture(object? sender, TouchEventArgs e) => StopPress(commit: true);

    private void OnTouchLeave(object? sender, TouchEventArgs e) => StopPress(commit: true);

    private void StartPress()
    {
        if (_pressed)
        {
            return;
        }

        var host = _host ?? DataContext as INumericFieldHost;
        var field = Field;
        if (host is null
            || string.IsNullOrEmpty(field)
            || !host.CanStepNumericField(field, Direction))
        {
            return;
        }

        _host = host;
        AttachHostEventsIfNeeded();
        _pressed = true;
        _suppressClick = true;
        _repeat.Press();

        if (!StepOnce())
        {
            StopPress(commit: true);
            return;
        }

        ScheduleNextTick();
    }

    private bool StepOnce()
    {
        var host = _host;
        var field = Field;
        if (host is null || string.IsNullOrEmpty(field))
        {
            return false;
        }

        var moved = host.StepNumericField(field, Direction);
        UpdateEnabled();
        return moved;
    }

    private void ScheduleNextTick()
    {
        var delay = _repeat.NextDelay;
        if (!_pressed || delay is null)
        {
            return;
        }

        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Input);
            _timer.Tick += OnTick;
        }

        _timer.Interval = delay.Value;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (!_pressed)
        {
            return;
        }

        var delay = _repeat.NextDelay;
        if (delay is not null)
        {
            _repeat.Advance(delay.Value);
        }

        if (!StepOnce())
        {
            StopPress(commit: true);
            return;
        }

        ScheduleNextTick();
    }

    private void StopPress(bool commit)
    {
        if (_timer is not null)
        {
            _timer.Stop();
        }

        _repeat.Release();
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        if (commit && _host is not null && Field is { Length: > 0 } field)
        {
            _host.CommitNumericStepHold(field);
        }

        UpdateEnabled();
    }

    private void AttachHostEventsIfNeeded()
    {
        if (_host is not null)
        {
            _host.PropertyChanged -= OnHostPropertyChanged;
            _host.PropertyChanged += OnHostPropertyChanged;
        }
    }
}
