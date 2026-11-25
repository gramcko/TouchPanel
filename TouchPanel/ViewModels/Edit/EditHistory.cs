namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// Bounded undo/redo stack of opaque snapshots for one edit session (user-requested undo/redo).
/// The class is WPF-free and knows nothing about pages or widgets: the caller supplies a serialized
/// snapshot for every committed change, and the history keeps the previous states so they can be
/// restored. A new change clears the redo stack, the stack size is bounded to
/// <see cref="Capacity"/> entries (the oldest entry is dropped), and <see cref="Reset"/> starts a
/// fresh session (used when edit mode is entered or exited).
/// </summary>
/// <remarks>
/// The API is deliberately split into "peek" and "apply" so the caller can restore the state first
/// and only then commit the stack movement; a restore that fails therefore leaves the history
/// untouched.
/// <para>
/// Scope: the snapshots cover the edited page's widgets (add/remove/duplicate/move/resize and
/// property/action/appearance/label edits). Page-structure changes (page add/rename/remove/open) are
/// not undoable; they run through the pages editor, which saves and ends the edit session, so the
/// history is already cleared before such a change completes.
/// </para>
/// </remarks>
public sealed class EditHistory
{
    /// <summary>Default maximum number of undo steps kept for one edit session.</summary>
    public const int DefaultCapacity = 50;

    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private string? _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="EditHistory"/> class.
    /// </summary>
    /// <param name="capacity">Maximum number of undo steps; hostile values fall back to <see cref="DefaultCapacity"/>.</param>
    public EditHistory(int capacity = DefaultCapacity)
    {
        Capacity = capacity > 0 ? capacity : DefaultCapacity;
    }

    /// <summary>Gets the maximum number of undo steps retained.</summary>
    public int Capacity { get; }

    /// <summary>Gets the number of available undo steps.</summary>
    public int UndoCount => _undo.Count;

    /// <summary>Gets the number of available redo steps.</summary>
    public int RedoCount => _redo.Count;

    /// <summary>Gets a value indicating whether an undo step is available.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Gets a value indicating whether a redo step is available.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Gets the snapshot that represents the current (last committed) state, or <see langword="null"/>
    /// when no session is active.
    /// </summary>
    public string? Current => _current;

    /// <summary>
    /// Starts a fresh session: clears both stacks and records the baseline state. Called when edit
    /// mode is entered or exited so history never crosses sessions.
    /// </summary>
    /// <param name="currentSnapshot">The snapshot of the state the session starts from, or <see langword="null"/> when no session is active.</param>
    public void Reset(string? currentSnapshot)
    {
        _undo.Clear();
        _redo.Clear();
        _current = currentSnapshot;
    }

    /// <summary>
    /// Records a new state after a committed edit: the previous current state becomes an undo step,
    /// the redo stack is cleared (a new edit invalidates the undone future), and the new state
    /// becomes current. Identical states are ignored, so idempotent applies never grow the history.
    /// </summary>
    /// <param name="nextSnapshot">The snapshot of the state after the edit; must not be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a new undo step was created.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="nextSnapshot"/> is <see langword="null"/>.</exception>
    public bool Record(string nextSnapshot)
    {
        ArgumentNullException.ThrowIfNull(nextSnapshot);

        if (_current is null)
        {
            // No session: adopt the state without creating an undo step (nothing to restore to).
            _current = nextSnapshot;
            return false;
        }

        if (string.Equals(_current, nextSnapshot, StringComparison.Ordinal))
        {
            return false;
        }

        Push(_undo, _current);
        _current = nextSnapshot;
        _redo.Clear();
        return true;
    }

    /// <summary>
    /// Moves the current snapshot forward in place without creating an undo step (user-requested live
    /// font-size preview): used for the previews after a group's first recorded change so undo still
    /// restores the state before the whole group while redo and a following group see the latest value.
    /// The undo and redo stacks are untouched.
    /// </summary>
    /// <param name="nextSnapshot">The latest applied state; must not be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the current snapshot changed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="nextSnapshot"/> is <see langword="null"/>.</exception>
    public bool ReplaceCurrent(string nextSnapshot)
    {
        ArgumentNullException.ThrowIfNull(nextSnapshot);

        if (string.Equals(_current, nextSnapshot, StringComparison.Ordinal))
        {
            return false;
        }

        _current = nextSnapshot;
        return true;
    }

    /// <summary>
    /// Gets the state an undo would restore without changing the stacks.
    /// </summary>
    /// <param name="snapshot">The previous state when available; otherwise an empty string.</param>
    /// <returns><see langword="true"/> when an undo step exists.</returns>
    public bool TryPeekUndo(out string snapshot)
    {
        if (_undo.Count == 0)
        {
            snapshot = string.Empty;
            return false;
        }

        snapshot = _undo[^1];
        return true;
    }

    /// <summary>
    /// Gets the state a redo would restore without changing the stacks.
    /// </summary>
    /// <param name="snapshot">The next state when available; otherwise an empty string.</param>
    /// <returns><see langword="true"/> when a redo step exists.</returns>
    public bool TryPeekRedo(out string snapshot)
    {
        if (_redo.Count == 0)
        {
            snapshot = string.Empty;
            return false;
        }

        snapshot = _redo[^1];
        return true;
    }

    /// <summary>
    /// Commits an undo after the caller restored the peeked state: the current state moves onto the
    /// redo stack and the peeked state becomes current. No-op when no undo step exists.
    /// </summary>
    /// <returns><see langword="true"/> when the undo was applied.</returns>
    public bool ApplyUndo()
    {
        if (!TryPeekUndo(out var snapshot))
        {
            return false;
        }

        _undo.RemoveAt(_undo.Count - 1);
        if (_current is not null)
        {
            Push(_redo, _current);
        }

        _current = snapshot;
        return true;
    }

    /// <summary>
    /// Commits a redo after the caller restored the peeked state: the current state moves back onto
    /// the undo stack and the peeked state becomes current. No-op when no redo step exists.
    /// </summary>
    /// <returns><see langword="true"/> when the redo was applied.</returns>
    public bool ApplyRedo()
    {
        if (!TryPeekRedo(out var snapshot))
        {
            return false;
        }

        _redo.RemoveAt(_redo.Count - 1);
        if (_current is not null)
        {
            Push(_undo, _current);
        }

        _current = snapshot;
        return true;
    }

    private void Push(List<string> stack, string snapshot)
    {
        stack.Add(snapshot);
        while (stack.Count > Capacity)
        {
            stack.RemoveAt(0);
        }
    }
}
