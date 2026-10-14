namespace TouchPanel.Services.Tray;

/// <summary>
/// Configuration for <see cref="TrayService"/>: what the icon shows and how registration
/// failures are retried. A record so callers can start from <see cref="Default"/> and override
/// individual values.
/// </summary>
public sealed record TrayServiceOptions
{
    /// <summary>The options used by the composition root unless overridden.</summary>
    public static TrayServiceOptions Default { get; } = new();

    /// <summary>Gets the tray tooltip shown on hover; defaults to <see cref="TrayService.DefaultTooltip"/>.</summary>
    public string Tooltip { get; init; } = TrayService.DefaultTooltip;

    /// <summary>
    /// Gets the icon handle (<c>HICON</c>) displayed in the notification area; 0 (the default)
    /// lets the shell fall back to its own default icon.
    /// </summary>
    public nint IconHandle { get; init; }

    /// <summary>Gets the bounded retry/backoff policy for failed registrations (RF-013).</summary>
    public TrayRegistrationRetryPolicy RetryPolicy { get; init; } = TrayRegistrationRetryPolicy.Default;
}
