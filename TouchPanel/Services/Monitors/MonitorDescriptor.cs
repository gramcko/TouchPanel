namespace TouchPanel.Services.Monitors;

/// <summary>
/// A rectangle in physical screen pixels (Win32 <c>RECT</c> semantics: virtual-desktop coordinates,
/// no DPI scaling applied). Deliberately WPF-free so monitor matching and placement planning stay
/// unit-testable without a UI (RNF-010, RF-016).
/// </summary>
/// <param name="X">Left edge in physical pixels.</param>
/// <param name="Y">Top edge in physical pixels.</param>
/// <param name="Width">Width in physical pixels.</param>
/// <param name="Height">Height in physical pixels.</param>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>Gets an empty rectangle (no usable bounds).</summary>
    public static PixelRect Empty => default;

    /// <summary>Gets a value indicating whether the rectangle has a positive width and height.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Gets the right edge (<see cref="X"/> + <see cref="Width"/>).</summary>
    public int Right => X + Width;

    /// <summary>Gets the bottom edge (<see cref="Y"/> + <see cref="Height"/>).</summary>
    public int Bottom => Y + Height;

    /// <inheritdoc />
    public override string ToString() => $"{Width}x{Height}@({X},{Y})";
}

/// <summary>
/// Immutable, WPF-free description of one active monitor, carrying every identity field RF-016 may
/// match on plus the physical bounds the panel needs (RF-014). Produced by
/// <see cref="IMonitorEnumerator"/> (production: <c>Win32MonitorEnumerator</c>) and consumed by
/// <see cref="MonitorMatcher"/> and the selection service.
/// </summary>
/// <remarks>
/// <para>Any identity field may be absent: virtual displays, display adapters without EDID, or
/// partial native failures produce descriptors with missing fields. The matcher treats absent
/// fields as unavailable, never as wildcards.</para>
/// <para><see cref="HardwareId"/> is the EDID-derived model identity (manufacturer + product code,
/// for example <c>DEL-A0BC</c>). <see cref="HardwareIdWithSerial"/> adds the best-effort unit token
/// extracted from the device path when present, so a configuration may pin a model or a specific
/// unit. Both are logged at discovery time (D-17) so the user can copy them into configuration.</para>
/// </remarks>
public sealed record MonitorDescriptor
{
    /// <summary>
    /// Gets the display device interface path from <c>QueryDisplayConfig</c> (for example
    /// <c>\\?\DISPLAY#DELA0BC#5&amp;2f1a3b4c&amp;0&amp;UID4353#{e6f07b5f-...}</c>), when available.
    /// This is RF-016 priority 2's "display device path".
    /// </summary>
    public string? DevicePath { get; init; }

    /// <summary>
    /// Gets the monitor device instance identifier from <c>EnumDisplayDevices</c> (for example
    /// <c>MONITOR\DELA0BC\{4d36e96e-...}\0001</c>), when available. Matched together with
    /// <see cref="DevicePath"/> so a configuration may store either form.
    /// </summary>
    public string? MonitorDeviceId { get; init; }

    /// <summary>Gets the EDID PnP manufacturer identifier (three letters, for example <c>DEL</c>), when available.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Gets the EDID product code as four uppercase hexadecimal digits (for example <c>A0BC</c>), when available.</summary>
    public string? ProductCode { get; init; }

    /// <summary>
    /// Gets the best-effort per-unit token extracted from the display device path (for example the
    /// <c>UID</c> suffix), when present. It is not guaranteed to be the raw EDID serial; it is a
    /// stable-enough unit discriminator for duplicate-model tiebreaks and optional unit pinning.
    /// </summary>
    public string? SerialNumber { get; init; }

    /// <summary>
    /// Gets the monitor-friendly name reported by Windows (for example <c>DELL U2415</c>), when
    /// available. This is RF-016 priority 3's "friendly name".
    /// </summary>
    public string? FriendlyName { get; init; }

    /// <summary>Gets the monitor's full physical bounds (<c>rcMonitor</c>), including any taskbar area (RF-014).</summary>
    public PixelRect Bounds { get; init; }

    /// <summary>Gets the monitor's work area (<c>rcWork</c>), excluding the taskbar.</summary>
    public PixelRect WorkArea { get; init; }

    /// <summary>Gets a value indicating whether this is the primary monitor.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Gets the source GDI device name (for example <c>\\.\DISPLAY1</c>), when available.</summary>
    public string? SourceGdiName { get; init; }

    /// <summary>
    /// Gets the canonical model-level hardware identity (manufacturer + product code, for example
    /// <c>DEL-A0BC</c>), or <see langword="null"/> when either part is missing. This is the value
    /// that belongs in <c>monitor.match.hardwareId</c> to pin a monitor model (RF-016 priority 1).
    /// </summary>
    public string? HardwareId =>
        !string.IsNullOrWhiteSpace(Manufacturer) && !string.IsNullOrWhiteSpace(ProductCode)
            ? $"{Manufacturer.Trim().ToUpperInvariant()}-{ProductCode.Trim().ToUpperInvariant()}"
            : null;

    /// <summary>
    /// Gets the unit-level hardware identity (<see cref="HardwareId"/> plus the best-effort serial
    /// token, for example <c>DEL-A0BC-4353</c>), or <see cref="HardwareId"/> when no serial token is
    /// available, or <see langword="null"/> when the model identity is unknown.
    /// </summary>
    public string? HardwareIdWithSerial =>
        HardwareId is { } hardwareId && !string.IsNullOrWhiteSpace(SerialNumber)
            ? $"{hardwareId}-{SerialNumber.Trim()}"
            : HardwareId;

    /// <summary>
    /// Gets a human-readable name for logs and notifications, preferring the friendly name, then
    /// the hardware identity, then the GDI name.
    /// </summary>
    public string DisplayName =>
        FirstNonEmpty(FriendlyName, HardwareIdWithSerial, SourceGdiName, DevicePath, MonitorDeviceId)
        ?? "unknown monitor";

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
