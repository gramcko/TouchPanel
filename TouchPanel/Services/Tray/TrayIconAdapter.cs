using TouchPanel.Interop;
using TouchPanel.Services.Notifications;

namespace TouchPanel.Services.Tray;

/// <summary>
/// Adapts the native <see cref="TouchPanel.Interop.TrayIcon"/> to the <see cref="ITrayIcon"/>
/// seam. This is the only type in <c>Services/Tray</c> that touches the Interop layer; it maps
/// the application-level <see cref="NotificationSeverity"/> to the Win32 balloon icon and
/// forwards events unchanged. It contains no logic beyond that mapping.
/// </summary>
public sealed class TrayIconAdapter : ITrayIcon
{
    private readonly Interop.TrayIcon _icon;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayIconAdapter"/> class.
    /// </summary>
    /// <param name="icon">
    /// The native icon wrapper; it must be created on the thread that owns the message pump
    /// (the WPF UI thread) and is owned by the adapter afterwards.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="icon"/> is <see langword="null"/>.</exception>
    public TrayIconAdapter(Interop.TrayIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);

        _icon = icon;
    }

    /// <inheritdoc />
    public bool IsRegistered => _icon.IsRegistered;

    /// <inheritdoc />
    public int LastError => _icon.LastError;

    /// <inheritdoc />
    public nint WindowHandle => _icon.WindowHandle;

    /// <inheritdoc />
    public event EventHandler<TrayIconMouseEventArgs>? MouseAction
    {
        add => _icon.MouseAction += value;
        remove => _icon.MouseAction -= value;
    }

    /// <inheritdoc />
    public event EventHandler? TaskbarCreated
    {
        add => _icon.TaskbarCreated += value;
        remove => _icon.TaskbarCreated -= value;
    }

    /// <inheritdoc />
    public bool Add(nint iconHandle, string? tooltip = null) => _icon.Add(iconHandle, tooltip);

    /// <inheritdoc />
    public bool ReAdd() => _icon.ReAdd();

    /// <inheritdoc />
    public bool Delete() => _icon.Delete();

    /// <inheritdoc />
    public bool ShowBalloon(string title, string message, NotificationSeverity severity) =>
        _icon.ShowBalloon(title, message, ToBalloonIcon(severity));

    /// <summary>Removes the icon (best effort) and destroys the hidden window owned by the native wrapper.</summary>
    public void Dispose() => _icon.Dispose();

    /// <summary>
    /// Maps the application-level severity to the Win32 balloon icon. Internal so the mapping is
    /// unit-testable without creating a native tray icon.
    /// </summary>
    /// <param name="severity">The notification severity.</param>
    /// <returns>The matching <see cref="TrayIconBalloonIcon"/>; unknown values fall back to <see cref="TrayIconBalloonIcon.Info"/>.</returns>
    internal static TrayIconBalloonIcon ToBalloonIcon(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Warning => TrayIconBalloonIcon.Warning,
        NotificationSeverity.Error => TrayIconBalloonIcon.Error,
        _ => TrayIconBalloonIcon.Info,
    };
}
