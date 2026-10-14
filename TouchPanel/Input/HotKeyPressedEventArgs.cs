namespace TouchPanel.Input;

/// <summary>
/// Payload of <see cref="IHotKeyRegistration.Pressed"/>: the numeric registration id carried by the
/// <c>WM_HOTKEY</c> message (<c>wParam</c>). The service maps it back to the binding that was
/// registered with that id (RF-058, M2-RF-583).
/// </summary>
public sealed class HotKeyPressedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HotKeyPressedEventArgs"/> class.
    /// </summary>
    /// <param name="id">The numeric registration id of the pressed hotkey.</param>
    public HotKeyPressedEventArgs(int id)
    {
        Id = id;
    }

    /// <summary>Gets the numeric registration id of the pressed hotkey.</summary>
    public int Id { get; }
}
