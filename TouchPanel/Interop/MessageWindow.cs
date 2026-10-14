using System.Windows.Interop;

namespace TouchPanel.Interop;

/// <summary>
/// Payload of <see cref="MessageWindow.MessageReceived"/>: the raw Win32 message and its
/// parameters, so consumers (single-instance signaling, tray) can interpret them.
/// </summary>
public sealed class MessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageReceivedEventArgs"/> class.
    /// </summary>
    /// <param name="message">The window message identifier.</param>
    /// <param name="wParam">The first message parameter.</param>
    /// <param name="lParam">The second message parameter.</param>
    public MessageReceivedEventArgs(int message, nint wParam, nint lParam)
    {
        Message = message;
        WParam = wParam;
        LParam = lParam;
    }

    /// <summary>Gets the window message identifier (for registered messages, the value from <see cref="MessageWindow.RegisterWindowMessage"/>).</summary>
    public int Message { get; }

    /// <summary>Gets the first message parameter.</summary>
    public nint WParam { get; }

    /// <summary>Gets the second message parameter.</summary>
    public nint LParam { get; }
}

/// <summary>
/// Owns a hidden window that exists only to receive Win32 messages (DD-2/DD-3). The window is
/// hosted by a WPF <see cref="HwndSource"/>, the deliberate, contained exception of plan
/// section 1.2 that lets the Interop layer use WPF window hosting while all raw P/Invoke stays
/// in <see cref="MessageWindowNative"/>. Contains no business logic.
/// <para>
/// By default the window is message-only (<c>HWND_MESSAGE</c>): invisible, non-enumerable, and
/// findable only through <see cref="FindMessageWindow"/> with <paramref name="messageOnly"/>
/// <see langword="true"/>. Such windows do <b>not</b> receive broadcast messages, so the
/// <see cref="TaskbarCreated"/> observation requires a window created with
/// <c>messageOnly: false</c> (a hidden top-level window); T-05 uses that mode for the tray.
/// </para>
/// The window must be created, used, and disposed on one STA thread (the WPF UI thread).
/// </summary>
public sealed class MessageWindow : IDisposable
{
    /// <summary>The registered message broadcast by the shell when the taskbar is (re)created (DD-2, RF-013).</summary>
    public const string TaskbarCreatedMessageName = "TaskbarCreated";

    private readonly HwndSource _source;
    private readonly HwndSourceHook _hook;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageWindow"/> class on the current thread.
    /// </summary>
    /// <param name="windowName">
    /// The window title. It is the stable lookup key used by <see cref="FindMessageWindow"/>, so it
    /// must be unique within the desktop (for example <c>TouchPanel.SingleInstance.SignalWindow.v1</c>).
    /// </param>
    /// <param name="messageOnly">
    /// <see langword="true"/> (default) to create a message-only window; <see langword="false"/> to
    /// create a hidden top-level window that also receives broadcast messages (required for
    /// <see cref="TaskbarCreated"/>).
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="windowName"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the current thread is not STA.</exception>
    public MessageWindow(string windowName, bool messageOnly = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowName);

        WindowName = windowName;
        MessageOnly = messageOnly;
        TaskbarCreatedMessageId = RegisterWindowMessage(TaskbarCreatedMessageName);

        var parameters = new HwndSourceParameters(windowName)
        {
            // A message-only window is parented to HWND_MESSAGE; the top-level mode stays hidden
            // (no WS_VISIBLE), is absent from Alt+Tab/taskbar, and never activates (RF-018/RF-019).
            ParentWindow = messageOnly ? MessageWindowNative.HwndMessage : 0,
            WindowStyle = messageOnly ? 0 : MessageWindowNative.WsPopup,
            ExtendedWindowStyle = messageOnly
                ? 0
                : MessageWindowNative.WsExToolWindow | MessageWindowNative.WsExNoActivate,
            Width = 0,
            Height = 0,
        };

        _source = new HwndSource(parameters);
        _hook = WndProc;
        _source.AddHook(_hook);
        Handle = _source.Handle;
    }

    /// <summary>Gets the window title used to find the window (<see cref="FindMessageWindow"/>).</summary>
    public string WindowName { get; }

    /// <summary>Gets a value indicating whether the window is message-only (<c>HWND_MESSAGE</c> parented).</summary>
    public bool MessageOnly { get; }

    /// <summary>Gets the native window handle (<c>HWND</c>).</summary>
    public nint Handle { get; }

    /// <summary>Gets the session-wide identifier of the <c>TaskbarCreated</c> message (0 when registration failed).</summary>
    public uint TaskbarCreatedMessageId { get; }

    /// <summary>Gets a value indicating whether the window has been destroyed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Raised for every message processed by the window (including registered messages).</summary>
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// Raised when the shell broadcasts <c>TaskbarCreated</c> (Explorer restart, RF-013).
    /// Only delivered when the window is not message-only; message-only windows do not receive broadcasts.
    /// </summary>
    public event EventHandler? TaskbarCreated;

    /// <summary>
    /// Registers a system-wide window message (<c>RegisterWindowMessage</c>). Registering the same
    /// name repeatedly returns the same identifier for every process in the session.
    /// </summary>
    /// <param name="messageName">The message name to register.</param>
    /// <returns>The message identifier; 0 when registration failed.</returns>
    public static uint RegisterWindowMessage(string messageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageName);

        return MessageWindowNative.RegisterWindowMessage(messageName);
    }

    /// <summary>
    /// Finds a TouchPanel message window by title, in either mode. Message-only windows cannot be
    /// found by <c>FindWindow</c>, so the lookup uses <c>FindWindowEx</c> with the
    /// <c>HWND_MESSAGE</c> parent.
    /// </summary>
    /// <param name="windowName">The window title passed to the constructor.</param>
    /// <param name="messageOnly">Whether the window was created as message-only (the default).</param>
    /// <returns>The window handle, or 0 when no matching window exists.</returns>
    public static nint FindMessageWindow(string windowName, bool messageOnly = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowName);

        return messageOnly
            ? MessageWindowNative.FindWindowEx(MessageWindowNative.HwndMessage, 0, null, windowName)
            : MessageWindowNative.FindWindow(null, windowName);
    }

    /// <summary>
    /// Posts a message to a window without waiting for the target thread to process it (DD-3).
    /// </summary>
    /// <param name="window">The target window handle.</param>
    /// <param name="message">The message identifier.</param>
    /// <param name="wParam">The first message parameter.</param>
    /// <param name="lParam">The second message parameter.</param>
    /// <returns><see langword="true"/> when the message was placed in the target's queue.</returns>
    public static bool PostMessage(nint window, uint message, nint wParam = 0, nint lParam = 0)
    {
        return window != 0 && MessageWindowNative.PostMessage(window, message, wParam, lParam);
    }

    /// <summary>
    /// Removes the message hook and destroys the window on the owning thread. Idempotent.
    /// Registered window messages have no unregister counterpart, so nothing else is released.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.RemoveHook(_hook);
        _source.Dispose();
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (TaskbarCreatedMessageId != 0 && (uint)message == TaskbarCreatedMessageId)
        {
            TaskbarCreated?.Invoke(this, EventArgs.Empty);
        }

        MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message, wParam, lParam));
        return 0;
    }
}
