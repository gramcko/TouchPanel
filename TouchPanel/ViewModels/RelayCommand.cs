using System.Windows.Input;

namespace TouchPanel.ViewModels;

/// <summary>
/// Minimal synchronous <see cref="ICommand"/> for immediate edit-UI operations (for example the
/// property panel's explicit <c>Apply</c> commit, M5-RF-531). Execution never blocks and any
/// failure is contained by the command body (the edit service logs and isolates projection
/// failures).
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The operation to run; must not be <see langword="null"/>.</param>
    /// <param name="canExecute">An optional predicate controlling enablement; <see langword="null"/> means always executable.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute"/> is <see langword="null"/>.</exception>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute();
        }
    }

    /// <summary>Raises <see cref="CanExecuteChanged"/> so bound controls re-evaluate enablement.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
