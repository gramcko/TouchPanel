using TouchPanel.Input;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IHotKeyRegistration"/> for hotkey tests (M2-RNF-124): it records every
/// registration attempt, can refuse selected virtual keys to simulate an already-taken combination,
/// tracks unregistrations, and lets tests raise the <see cref="Pressed"/> event. It never touches
/// the real <c>RegisterHotKey</c> API, so no system hotkey is captured.
/// </summary>
internal sealed class FakeHotKeyRegistration : IHotKeyRegistration
{
    /// <summary>Gets every successful registration, in order (accumulated across applies).</summary>
    public List<Registration> Registrations { get; } = [];

    /// <summary>Gets the ids passed to <see cref="TryUnregister"/>, in order.</summary>
    public List<int> Unregistrations { get; } = [];

    /// <summary>Gets the virtual keys whose registration should be refused (simulates "already taken").</summary>
    public HashSet<ushort> FailedVirtualKeys { get; } = [];

    /// <summary>
    /// Gets or sets a probe invoked on every registration attempt; tests use it to assert the call
    /// ran on the expected (UI) thread.
    /// </summary>
    public Func<bool>? ExpectedThreadCheck { get; set; }

    /// <summary>Gets the result of <see cref="ExpectedThreadCheck"/> for each attempt, in order.</summary>
    public List<bool> OnExpectedThread { get; } = [];

    /// <summary>Gets the number of registration attempts (proves there is no retry loop).</summary>
    public int RegisterCallCount { get; private set; }

    /// <summary>Gets a value indicating whether <see cref="Dispose"/> was called.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc />
    public event EventHandler<HotKeyPressedEventArgs>? Pressed;

    /// <summary>Gets the currently registered ids (added on success, removed on unregister).</summary>
    public HashSet<int> RegisteredIds { get; } = [];

    /// <inheritdoc />
    public bool TryRegister(int id, HotKeyModifiers modifiers, ushort virtualKey)
    {
        RegisterCallCount++;
        OnExpectedThread.Add(ExpectedThreadCheck?.Invoke() ?? true);

        if (FailedVirtualKeys.Contains(virtualKey))
        {
            return false;
        }

        Registrations.Add(new Registration(id, modifiers, virtualKey));
        RegisteredIds.Add(id);
        return true;
    }

    /// <inheritdoc />
    public bool TryUnregister(int id)
    {
        Unregistrations.Add(id);
        RegisteredIds.Remove(id);
        return true;
    }

    /// <summary>Raises <see cref="Pressed"/> for the given registration id.</summary>
    /// <param name="id">The registration id to report.</param>
    public void RaisePressed(int id) => Pressed?.Invoke(this, new HotKeyPressedEventArgs(id));

    /// <inheritdoc />
    public void Dispose() => Disposed = true;

    /// <summary>One recorded registration attempt.</summary>
    internal sealed record Registration(int Id, HotKeyModifiers Modifiers, ushort VirtualKey);
}
