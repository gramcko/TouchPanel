using System.Windows.Input;

namespace TouchPanel.ViewModels;

/// <summary>
/// Minimal <see cref="ICommand"/> used by widget view models to expose a single "completed tap"
/// interaction to the templates (RF-035). Execution is asynchronous and never blocks the UI
/// thread (RF-050/RNF-004); any failure is contained here as a last-resort guard because the view
/// model already converts action failures into a transient error state and a log entry (RF-036).
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The asynchronous interaction to run; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute"/> is <see langword="null"/>.</exception>
    public AsyncRelayCommand(Func<Task> execute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Widget enablement is expressed through the control's <c>IsEnabled</c> binding, so the
    /// command itself is always executable; disabled view models no-op inside their activation
    /// method (RF-047).
    /// </remarks>
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => true;

    /// <inheritdoc />
    public async void Execute(object? parameter)
    {
        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // RF-050: a command invocation must never surface an exception to the dispatcher.
            // The view model owns error signalling; this guard only keeps the UI alive.
        }
    }
}
