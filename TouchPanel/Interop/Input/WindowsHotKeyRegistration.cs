using TouchPanel.Input;

namespace TouchPanel.Interop.Input;

/// <summary>
/// The production <see cref="IHotKeyRegistration"/> (RF-058, M2-RF-582): it owns a hidden top-level
/// <see cref="MessageWindow"/> created on the UI thread, registers combinations against its handle,
/// and translates <c>WM_HOTKEY</c> into <see cref="IHotKeyRegistration.Pressed"/>. The window is
/// non-message-only so the registered hotkey messages are posted to its queue and pumped; it is
/// hidden, <c>WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE</c>, and never activates the panel (RF-018).
/// </summary>
internal sealed class WindowsHotKeyRegistration : IHotKeyRegistration
{
    /// <summary>The default unique desktop window name used for the hidden hotkey window.</summary>
    public const string DefaultWindowName = "TouchPanel.HotKeys.v1";

    private readonly MessageWindow _window;
    private readonly HashSet<int> _registered = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsHotKeyRegistration"/> class. Must be
    /// called on the UI (STA) thread because it creates a WPF-hosted window.
    /// </summary>
    /// <param name="windowName">The unique hidden-window name; defaults to <see cref="DefaultWindowName"/>.</param>
    public WindowsHotKeyRegistration(string? windowName = null)
    {
        _window = new MessageWindow(windowName ?? DefaultWindowName, messageOnly: false);
        _window.MessageReceived += OnMessageReceived;
    }

    /// <inheritdoc />
    public event EventHandler<HotKeyPressedEventArgs>? Pressed;

    /// <inheritdoc />
    public bool TryRegister(int id, HotKeyModifiers modifiers, ushort virtualKey)
    {
        if (_disposed || _window.Handle == nint.Zero)
        {
            return false;
        }

        if (!HotKeyNative.RegisterHotKey(_window.Handle, id, (uint)modifiers, virtualKey))
        {
            return false;
        }

        _registered.Add(id);
        return true;
    }

    /// <inheritdoc />
    public bool TryUnregister(int id)
    {
        if (_disposed)
        {
            return false;
        }

        var released = HotKeyNative.UnregisterHotKey(_window.Handle, id);
        _registered.Remove(id);
        return released;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var id in _registered)
        {
            HotKeyNative.UnregisterHotKey(_window.Handle, id);
        }

        _registered.Clear();
        _window.MessageReceived -= OnMessageReceived;
        _window.Dispose();
        _disposed = true;
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        if (e.Message == HotKeyNative.WmHotKey)
        {
            Pressed?.Invoke(this, new HotKeyPressedEventArgs((int)e.WParam));
        }
    }
}
