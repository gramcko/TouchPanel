using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TouchPanel.Widgets;

/// <summary>
/// A touch-first <see cref="Slider"/> that raises <see cref="InteractionCompletedCommand"/> once
/// the user finishes dragging the handle (touch or mouse), so the slider view model can emit the
/// final value exactly once without any per-instance code-behind (T-06/M2-RF-402). It is a
/// reusable control used by the Slider <c>DataTemplate</c>; all value/geometry logic stays on the
/// view model.
/// </summary>
/// <remarks>
/// WPF promotes touch input to mouse, so <see cref="Thumb.DragCompletedEvent"/> fires for touch
/// drags as well as mouse drags. The event bubbles from the control's internal thumb up to this
/// element, which forwards it to the bound command.
/// </remarks>
public sealed class TouchSlider : Slider
{
    /// <summary>Identifies the <see cref="InteractionCompletedCommand"/> dependency property.</summary>
    public static readonly DependencyProperty InteractionCompletedCommandProperty =
        DependencyProperty.Register(
            nameof(InteractionCompletedCommand),
            typeof(ICommand),
            typeof(TouchSlider),
            new PropertyMetadata(null));

    /// <summary>
    /// Initializes a new instance of the <see cref="TouchSlider"/> class and subscribes to the
    /// thumb drag-completed routed event.
    /// </summary>
    public TouchSlider()
    {
        AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted));
    }

    /// <summary>
    /// Gets or sets the command raised once per completed drag/tap gesture. It replaces nothing in
    /// the base <see cref="Slider"/>; the view model uses it to emit the final value.
    /// </summary>
    public ICommand? InteractionCompletedCommand
    {
        get => (ICommand?)GetValue(InteractionCompletedCommandProperty);
        set => SetValue(InteractionCompletedCommandProperty, value);
    }

    private void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        var command = InteractionCompletedCommand;
        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
