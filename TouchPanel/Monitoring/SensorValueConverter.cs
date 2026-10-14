namespace TouchPanel.Monitoring;

/// <summary>
/// The hardware-library unit conventions the adapter converts from (RF-063): values already in
/// application units (percent, °C, RPM, MHz, V, A, W) pass through unchanged, while memory amounts
/// and throughput rates are converted to bytes and bytes per second. Kept free of
/// LibreHardwareMonitor types so the conversion table is unit-testable without hardware (RF-062).
/// </summary>
internal enum SensorValueUnit
{
    /// <summary>A percentage (identity).</summary>
    Percent,

    /// <summary>Degrees Celsius (identity).</summary>
    Celsius,

    /// <summary>Revolutions per minute (identity).</summary>
    Rpm,

    /// <summary>Megahertz (identity).</summary>
    Megahertz,

    /// <summary>Volts (identity).</summary>
    Volt,

    /// <summary>Amperes (identity).</summary>
    Ampere,

    /// <summary>Watts (identity).</summary>
    Watt,

    /// <summary>Gigabytes (converted to bytes).</summary>
    Gigabytes,

    /// <summary>Megabytes (converted to bytes).</summary>
    Megabytes,

    /// <summary>Kilobytes (converted to bytes).</summary>
    Kilobytes,

    /// <summary>Bytes per second (identity; LibreHardwareMonitor's network throughput convention).</summary>
    BytesPerSecond,

    /// <summary>Megabytes per second (converted to bytes per second).</summary>
    MegabytesPerSecond,

    /// <summary>Kilobytes per second (converted to bytes per second).</summary>
    KilobytesPerSecond,
}

/// <summary>
/// Converts one raw hardware-library value into the application unit for its field (RF-063):
/// memory to bytes, throughput to bytes per second, and the scalar units (percent, °C, RPM, MHz,
/// V, A, W) through unchanged. A missing value stays <see langword="null"/> - never a fabricated
/// zero (RF-065).
/// </summary>
internal static class SensorValueConverter
{
    private const double Kibibyte = 1024d;
    private const double Mebibyte = Kibibyte * 1024d;
    private const double Gibibyte = Mebibyte * 1024d;

    /// <summary>
    /// Converts a raw value from its hardware-library convention to the application unit.
    /// </summary>
    /// <param name="value">The raw value, or <see langword="null"/> when unavailable.</param>
    /// <param name="unit">The hardware-library convention of the value.</param>
    /// <returns>The application value, or <see langword="null"/> when the input was missing.</returns>
    public static double? ToApplicationValue(double? value, SensorValueUnit unit) => unit switch
    {
        SensorValueUnit.Gigabytes => Scale(value, Gibibyte),
        SensorValueUnit.Megabytes => Scale(value, Mebibyte),
        SensorValueUnit.Kilobytes => Scale(value, Kibibyte),
        SensorValueUnit.MegabytesPerSecond => Scale(value, Mebibyte),
        SensorValueUnit.KilobytesPerSecond => Scale(value, Kibibyte),

        // Percent, Celsius, Rpm, Megahertz, Volt, Ampere, Watt, and BytesPerSecond already use the
        // application unit.
        _ => value,
    };

    private static double? Scale(double? value, double factor) => value is { } actual ? actual * factor : null;
}
