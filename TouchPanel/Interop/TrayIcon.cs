using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TouchPanel.Interop;

/// <summary>
/// Balloon icon drawn with a tray notification (maps to the <c>NIIF_*</c> flags of
/// <c>NOTIFYICONDATA</c>).
/// </summary>
public enum TrayIconBalloonIcon
{
    /// <summary>No icon (<c>NIIF_NONE</c>).</summary>
    None = 0,

    /// <summary>Information icon (<c>NIIF_INFO</c>).</summary>
    Info = 1,

    /// <summary>Warning icon (<c>NIIF_WARNING</c>).</summary>
    Warning = 2,

    /// <summary>Error icon (<c>NIIF_ERROR</c>).</summary>
    Error = 3,
}

/// <summary>
/// Payload of <see cref="TrayIcon.MouseAction"/>: one decoded tray mouse message and the cursor
/// position when the shell reports it (version-4 callbacks; 0 for legacy callbacks).
/// </summary>
public sealed class TrayIconMouseEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrayIconMouseEventArgs"/> class.
    /// </summary>
    /// <param name="message">The Win32 mouse message (for example <see cref="TrayIcon.WmRButtonUp"/>).</param>
    /// <param name="x">The cursor X screen coordinate, when reported.</param>
    /// <param name="y">The cursor Y screen coordinate, when reported.</param>
    public TrayIconMouseEventArgs(int message, int x, int y)
    {
        Message = message;
        X = x;
        Y = y;
    }

    /// <summary>Gets the Win32 mouse message identifier (one of the <c>WM_*BUTTON*</c> constants on <see cref="TrayIcon"/>).</summary>
    public int Message { get; }

    /// <summary>Gets the cursor X screen coordinate; 0 for legacy callbacks without position data.</summary>
    public int X { get; }

    /// <summary>Gets the cursor Y screen coordinate; 0 for legacy callbacks without position data.</summary>
    public int Y { get; }
}

/// <summary>
/// Thin native <c>Shell_NotifyIcon</c> wrapper (DD-2): adds/modifies/deletes one tray icon,
/// raises menu-relevant mouse actions received on its hidden top-level window
/// (<c>WM_TRAYICON</c>), and forwards the shell's <c>TaskbarCreated</c> broadcast so the owner
/// can re-register after an Explorer restart (RF-013). Balloons are shown via <c>NIF_INFO</c>
/// and are non-activating.
/// <para>
/// Contains no business logic: no menu, no retry/backoff (owned by <c>TrayService</c> in T-05
/// part 2). All P/Invoke for the tray lives in this file (RNF-010). Create it on the thread that
/// owns the message pump (the WPF UI thread); the hidden window never activates and never
/// appears in the taskbar or Alt+Tab (RF-018/RF-019).
/// </para>
/// </summary>
public sealed class TrayIcon : IDisposable
{
    /// <summary>Title of the hidden top-level window that receives the icon callbacks.</summary>
    public const string WindowName = "TouchPanel.TrayIcon.Window.v1";

    /// <summary>The <c>WM_TRAYICON</c> callback message (<c>WM_APP + 1</c>) sent to the hidden window.</summary>
    public const int CallbackMessage = 0x8000 + 1;

    /// <summary>Default identifier of the icon within the hidden window (<c>NOTIFYICONDATA.uID</c>).</summary>
    public const uint DefaultIconId = 1;

    /// <summary>The left mouse button was pressed on the icon (<c>WM_LBUTTONDOWN</c>).</summary>
    public const int WmLButtonDown = 0x0201;

    /// <summary>The left mouse button was released on the icon (<c>WM_LBUTTONUP</c>).</summary>
    public const int WmLButtonUp = 0x0202;

    /// <summary>The left mouse button was double-clicked on the icon (<c>WM_LBUTTONDBLCLK</c>).</summary>
    public const int WmLButtonDblClk = 0x0203;

    /// <summary>The right mouse button was pressed on the icon (<c>WM_RBUTTONDOWN</c>).</summary>
    public const int WmRButtonDown = 0x0204;

    /// <summary>The right mouse button was released on the icon (<c>WM_RBUTTONUP</c>).</summary>
    public const int WmRButtonUp = 0x0205;

    /// <summary>The right mouse button was double-clicked on the icon (<c>WM_RBUTTONDBLCLK</c>).</summary>
    public const int WmRButtonDblClk = 0x0206;

    /// <summary>The left-click/select notification (<c>NIN_SELECT</c>, alias of <c>WM_USER</c>) in the version-4 layout.</summary>
    public const int NinSelect = 0x0400;

    /// <summary>The keyboard-select notification (<c>NIN_KEYSELECT</c>) in the version-4 layout.</summary>
    public const int NinKeySelect = 0x0401;

    /// <summary>The right-click/context-menu notification (<c>WM_CONTEXTMENU</c>) in the version-4 layout.</summary>
    public const int WmContextMenu = 0x007B;

    // NOTIFYICONDATA fixed-buffer limits (Win32 counts the terminating null character).
    internal const int TooltipMaxLength = 127;
    internal const int BalloonTitleMaxLength = 63;
    internal const int BalloonTextMaxLength = 255;

    // Shell_NotifyIcon message identifiers.
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;

    // NOTIFYICONDATA.uFlags values used by this wrapper.
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;

    // Callback layout requested after registration (wParam/lParam carry message and position).
    private const uint NotifyIconVersion4 = 4;

    /// <summary>Marshaled size of the full <c>NOTIFYICONDATAW</c> structure for this process; asserted by tests.</summary>
    internal static readonly int NativeStructSize = Marshal.SizeOf<NotifyIconData>();

    private readonly MessageWindow _window;
    private readonly bool _ownsWindow;
    private readonly uint _iconId;
    private nint _iconHandle;
    private string _tooltip = string.Empty;
    private bool _hasSettings;
    private bool _added;
    private bool _usesVersion4;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayIcon"/> class, creating its hidden
    /// top-level message window (or subscribing to an injected one). A top-level window is
    /// required so the shell's <c>TaskbarCreated</c> broadcast is received (RF-013); it is never
    /// shown, never activates, and is absent from the taskbar and Alt+Tab.
    /// </summary>
    /// <param name="iconId">Identifier of the icon within the window; defaults to <see cref="DefaultIconId"/>.</param>
    /// <param name="messageWindow">
    /// Optional externally owned message window to receive the callbacks. When
    /// <see langword="null"/> (the default) the icon creates and owns a hidden top-level window.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="messageWindow"/> is message-only: such windows do not receive
    /// broadcasts, so <c>TaskbarCreated</c> re-registration (RF-013) would silently never work.
    /// </exception>
    public TrayIcon(uint iconId = DefaultIconId, MessageWindow? messageWindow = null)
    {
        if (messageWindow is { MessageOnly: true })
        {
            throw new ArgumentException(
                "The tray icon requires a top-level message window so TaskbarCreated broadcasts are received.",
                nameof(messageWindow));
        }

        _iconId = iconId;
        _ownsWindow = messageWindow is null;
        _window = messageWindow ?? new MessageWindow(WindowName, messageOnly: false);
        _window.MessageReceived += OnMessageReceived;
        _window.TaskbarCreated += OnTaskbarCreated;
    }

    /// <summary>Gets a value indicating whether the shell currently shows the icon (RF-013).</summary>
    public bool IsRegistered => _added;

    /// <summary>Gets the Win32 error code of the last unsuccessful shell call (0 after success).</summary>
    public int LastError { get; private set; }

    /// <summary>Gets the handle of the hidden window that receives the callbacks.</summary>
    public nint WindowHandle => _window.Handle;

    /// <summary>
    /// Raised for menu-relevant mouse messages on the icon: left/right button up (the primary
    /// menu triggers), down, and double-click. Handlers run on the thread that pumps the hidden
    /// window's messages (the WPF UI thread) and must not activate other windows (RF-024).
    /// </summary>
    public event EventHandler<TrayIconMouseEventArgs>? MouseAction;

    /// <summary>
    /// Raised when the shell broadcasts <c>TaskbarCreated</c> (Explorer restarted, RF-013).
    /// The registration state has been reset; the owner re-registers with <see cref="ReAdd"/>
    /// under its own retry/backoff policy (DD-2).
    /// </summary>
    public event EventHandler? TaskbarCreated;

    /// <summary>
    /// Registers the icon with the shell (<c>NIM_ADD</c>) with the callback message, icon, and
    /// tooltip set, then requests <c>NOTIFYICON_VERSION_4</c> callback semantics.
    /// </summary>
    /// <param name="iconHandle">Handle of the icon to display (an <c>HICON</c>); 0 uses the shell default.</param>
    /// <param name="tooltip">Tray tooltip; values longer than the Win32 limit are truncated.</param>
    /// <returns><see langword="true"/> when the shell accepted the registration.</returns>
    public bool Add(nint iconHandle, string? tooltip = null)
    {
        ThrowIfDisposed();

        _iconHandle = iconHandle;
        _tooltip = tooltip ?? string.Empty;
        _hasSettings = true;

        var data = CreateData(NifMessage | NifIcon | NifTip);
        data.hIcon = iconHandle;
        data.szTip = Truncate(_tooltip, TooltipMaxLength);

        var added = ShellNotifyIcon(NimAdd, ref data);
        UpdateLastError(added);
        _added = added;

        if (added)
        {
            // Only a successful NIM_SETVERSION switches callbacks to the version-4 layout; until
            // then (and when the shell rejects it) the legacy layout is in force.
            _usesVersion4 = TrySetVersion4();
        }

        return added;
    }

    /// <summary>
    /// Updates the icon and tooltip (<c>NIM_MODIFY</c>). A <see langword="null"/> tooltip keeps
    /// the last tooltip supplied. No shell call is made while the icon is not registered.
    /// </summary>
    /// <param name="iconHandle">Handle of the new icon.</param>
    /// <param name="tooltip">New tooltip, or <see langword="null"/> to keep the current one.</param>
    /// <returns><see langword="true"/> when the shell accepted the update.</returns>
    public bool Modify(nint iconHandle, string? tooltip = null)
    {
        ThrowIfDisposed();

        if (!_added)
        {
            return false;
        }

        _iconHandle = iconHandle;
        if (tooltip is not null)
        {
            _tooltip = tooltip;
        }

        _hasSettings = true;

        var data = CreateData(NifMessage | NifIcon | NifTip);
        data.hIcon = iconHandle;
        data.szTip = Truncate(_tooltip, TooltipMaxLength);

        var modified = ShellNotifyIcon(NimModify, ref data);
        UpdateLastError(modified);
        return modified;
    }

    /// <summary>
    /// Re-registers the icon with its last settings (<c>NIM_ADD</c>): the recovery path for a
    /// <c>TaskbarCreated</c> broadcast (RF-013). Fails without a shell call when no icon has ever
    /// been registered successfully (or after <see cref="Delete"/> cleared the settings).
    /// </summary>
    /// <returns><see langword="true"/> when the shell accepted the re-registration.</returns>
    public bool ReAdd()
    {
        ThrowIfDisposed();

        return _hasSettings && Add(_iconHandle, _tooltip);
    }

    /// <summary>
    /// Removes the icon from the shell (<c>NIM_DELETE</c>) and forgets the stored settings.
    /// Safe to call repeatedly.
    /// </summary>
    /// <returns><see langword="true"/> when the icon is no longer registered.</returns>
    public bool Delete()
    {
        ThrowIfDisposed();
        return DeleteCore();
    }

    /// <summary>
    /// Shows a balloon notification on the icon (<c>NIF_INFO</c>). The shell displays it without
    /// taking foreground focus. Does nothing (returns <see langword="false"/> without a shell
    /// call) while the icon is not registered, so callers can fall back to logging.
    /// </summary>
    /// <param name="title">Balloon title; values longer than the Win32 limit are truncated.</param>
    /// <param name="message">Balloon body; values longer than the Win32 limit are truncated.</param>
    /// <param name="icon">Balloon icon; defaults to <see cref="TrayIconBalloonIcon.Info"/>.</param>
    /// <returns><see langword="true"/> when the shell accepted the balloon.</returns>
    public bool ShowBalloon(
        string title,
        string message,
        TrayIconBalloonIcon icon = TrayIconBalloonIcon.Info)
    {
        ThrowIfDisposed();

        if (!_added)
        {
            return false;
        }

        var data = CreateData(NifInfo);
        data.szInfoTitle = Truncate(title, BalloonTitleMaxLength);
        data.szInfo = Truncate(message, BalloonTextMaxLength);
        data.dwInfoFlags = (uint)icon;

        var shown = ShellNotifyIcon(NimModify, ref data);
        UpdateLastError(shown);
        return shown;
    }

    /// <summary>
    /// Removes the icon (best effort) and destroys the hidden window when this instance owns it.
    /// Idempotent; never throws (the tray must not take the application down at shutdown).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            DeleteCore();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"TouchPanel: removing the tray icon failed ({ex.GetType().Name}: {ex.Message})");
        }

        _window.MessageReceived -= OnMessageReceived;
        _window.TaskbarCreated -= OnTaskbarCreated;

        if (_ownsWindow)
        {
            try
            {
                _window.Dispose();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"TouchPanel: destroying the tray message window failed ({ex.GetType().Name}: {ex.Message})");
            }
        }
    }

    /// <summary>
    /// Decodes the raw <c>WM_TRAYICON</c> parameters into a menu mouse action using the callback
    /// layout negotiated with the shell.
    /// <para>
    /// With <c>NOTIFYICON_VERSION_4</c> (<paramref name="usesVersion4"/> is <see langword="true"/>)
    /// the parameters are <c>wParam = MAKELPARAM(x, y)</c> (screen coordinates, each a signed
    /// 16-bit value) and <c>lParam = MAKELPARAM(event, iconId)</c>. Only <c>NIN_SELECT</c> and
    /// <c>NIN_KEYSELECT</c> (mapped to <see cref="WmLButtonUp"/>) and <c>WM_CONTEXTMENU</c>
    /// (mapped to <see cref="WmRButtonUp"/>) open the menu; balloon and popup events
    /// (<c>NIN_BALLOON*</c>/<c>NIN_POPUP*</c>) are ignored.
    /// </para>
    /// <para>
    /// Without version 4 the legacy layout applies: <c>wParam</c> is the icon id and
    /// <c>lParam</c> is the <c>WM_*BUTTON*</c> mouse message; coordinates are unavailable (0).
    /// </para>
    /// </summary>
    /// <param name="wParam">The first callback parameter.</param>
    /// <param name="lParam">The second callback parameter.</param>
    /// <param name="usesVersion4">Whether <c>NIM_SETVERSION(4)</c> was successfully negotiated.</param>
    /// <returns>The decoded action, or <see langword="null"/> when it is not a menu trigger.</returns>
    internal static TrayIconMouseEventArgs? DecodeMouseAction(nint wParam, nint lParam, bool usesVersion4)
    {
        if (!usesVersion4)
        {
            // Legacy callback: wParam is the icon id, lParam carries the mouse message.
            var legacyMessage = unchecked((int)lParam);
            return IsMenuMouseAction(legacyMessage)
                ? new TrayIconMouseEventArgs(legacyMessage, 0, 0)
                : null;
        }

        // Version-4 layout: wParam = MAKELPARAM(x, y); lParam = MAKELPARAM(event, iconId).
        var x = unchecked((short)((ulong)wParam & 0xFFFF));
        var y = unchecked((short)(((ulong)wParam >> 16) & 0xFFFF));
        var notification = (int)((ulong)lParam & 0xFFFF);

        var message = notification switch
        {
            NinSelect or NinKeySelect => WmLButtonUp,
            WmContextMenu => WmRButtonUp,
            _ => 0,
        };

        return message == 0 ? null : new TrayIconMouseEventArgs(message, x, y);
    }

    /// <summary>
    /// Truncates a string to a fixed-buffer limit, never splitting a surrogate pair and never
    /// returning <see langword="null"/>.
    /// </summary>
    /// <param name="value">The value to truncate.</param>
    /// <param name="maxLength">The maximum number of UTF-16 code units allowed.</param>
    /// <returns>The original value or a safe prefix.</returns>
    internal static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        var length = maxLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    private static bool IsMenuMouseAction(int message) =>
        message is WmLButtonDown or WmLButtonUp or WmLButtonDblClk
            or WmRButtonDown or WmRButtonUp or WmRButtonDblClk;

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        if (e.Message != CallbackMessage)
        {
            return;
        }

        var action = DecodeMouseAction(e.WParam, e.LParam, _usesVersion4);
        if (action is not null)
        {
            MouseAction?.Invoke(this, action);
        }
    }

    private void OnTaskbarCreated(object? sender, EventArgs e)
    {
        // The shell discarded all icons when Explorer restarted; the cached registration is stale
        // until the owner re-adds it (RF-013).
        _added = false;
        TaskbarCreated?.Invoke(this, EventArgs.Empty);
    }

    private bool DeleteCore()
    {
        if (!_added)
        {
            _hasSettings = false;
            return true;
        }

        var data = CreateData(0);
        var deleted = ShellNotifyIcon(NimDelete, ref data);
        UpdateLastError(deleted);
        _added = false;
        _hasSettings = false;
        return deleted;
    }

    private bool TrySetVersion4()
    {
        // Failure leaves legacy callback semantics, which DecodeMouseAction also handles; it does
        // not invalidate the registration, so the error is deliberately not surfaced, but the
        // result decides which layout DecodeMouseAction must expect.
        var data = CreateData(0);
        data.uTimeoutOrVersion = NotifyIconVersion4;
        return ShellNotifyIcon(NimSetVersion, ref data);
    }

    private NotifyIconData CreateData(uint flags) => new()
    {
        cbSize = (uint)NativeStructSize,
        hWnd = _window.Handle,
        uID = _iconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void UpdateLastError(bool succeeded)
    {
        LastError = succeeded ? 0 : Marshal.GetLastWin32Error();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
}
