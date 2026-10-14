using TouchPanel.Interop;

namespace TouchPanel.Services.SingleInstance;

/// <summary>
/// DD-3 signaling channel over a hidden message-only window: the primary registers
/// <c>TouchPanel.ShowPanel.v1</c> and <c>TouchPanel.Exit.v1</c> on a <see cref="MessageWindow"/>
/// and listens for both; the secondary locates the primary's window (<c>FindWindowEx</c>) and
/// posts the matching message to it. Native work is delegated to <see cref="MessageWindow"/>,
/// keeping the service logic free of P/Invoke and WPF.
/// </summary>
public sealed class Win32SingleInstanceChannel : ISingleInstanceChannel
{
    private readonly string _windowName;
    private uint _showPanelMessageId;
    private uint _exitMessageId;
    private MessageWindow? _window;
    private Action? _onShowPanelRequested;
    private Action? _onExitRequested;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Win32SingleInstanceChannel"/> class.
    /// The message identifiers are registered lazily, so construction is side-effect free.
    /// </summary>
    /// <param name="windowName">The title of the hidden signal window; defaults to the name fixed by DD-3.</param>
    public Win32SingleInstanceChannel(string windowName = SingleInstanceService.SignalWindowName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowName);

        _windowName = windowName;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when a window message cannot be registered or the window cannot be created (for
    /// example on a non-STA thread). The service logs this and keeps running.
    /// </exception>
    public void StartListening(Action onShowPanelRequested, Action onExitRequested)
    {
        ArgumentNullException.ThrowIfNull(onShowPanelRequested);
        ArgumentNullException.ThrowIfNull(onExitRequested);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_window is not null)
        {
            return;
        }

        var showPanelMessageId = EnsureShowPanelMessageId();
        if (showPanelMessageId == 0)
        {
            throw new InvalidOperationException(
                $"Registering the window message '{SingleInstanceService.ShowPanelMessageName}' failed.");
        }

        var exitMessageId = EnsureExitMessageId();
        if (exitMessageId == 0)
        {
            throw new InvalidOperationException(
                $"Registering the window message '{SingleInstanceService.ExitMessageName}' failed.");
        }

        _onShowPanelRequested = onShowPanelRequested;
        _onExitRequested = onExitRequested;
        _window = new MessageWindow(_windowName);
        _window.MessageReceived += OnMessageReceived;
    }

    /// <inheritdoc />
    public bool TrySignalRunningInstance() => TryPost(EnsureShowPanelMessageId());

    /// <inheritdoc />
    public bool TrySignalExit() => TryPost(EnsureExitMessageId());

    /// <summary>Destroys the listening window; idempotent and safe to call from any state.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_window is not null)
        {
            _window.MessageReceived -= OnMessageReceived;
            _window.Dispose();
            _window = null;
        }

        _onShowPanelRequested = null;
        _onExitRequested = null;
    }

    private bool TryPost(uint messageId)
    {
        if (_disposed || messageId == 0)
        {
            return false;
        }

        var target = MessageWindow.FindMessageWindow(_windowName);
        if (target == 0)
        {
            return false;
        }

        return MessageWindow.PostMessage(target, messageId);
    }

    private uint EnsureShowPanelMessageId()
    {
        if (_showPanelMessageId == 0)
        {
            _showPanelMessageId = MessageWindow.RegisterWindowMessage(SingleInstanceService.ShowPanelMessageName);
        }

        return _showPanelMessageId;
    }

    private uint EnsureExitMessageId()
    {
        if (_exitMessageId == 0)
        {
            _exitMessageId = MessageWindow.RegisterWindowMessage(SingleInstanceService.ExitMessageName);
        }

        return _exitMessageId;
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        var message = (uint)e.Message;

        if (message == _showPanelMessageId && _showPanelMessageId != 0)
        {
            _onShowPanelRequested?.Invoke();
            return;
        }

        if (message == _exitMessageId && _exitMessageId != 0)
        {
            _onExitRequested?.Invoke();
        }
    }
}
