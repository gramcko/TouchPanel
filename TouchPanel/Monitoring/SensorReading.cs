namespace TouchPanel.Monitoring;

/// <summary>
/// Unit of an application-level sensor reading, used by the Gauge widget to format values without
/// knowing anything about the hardware library (RF-062/RF-063).
/// </summary>
public enum SensorUnit
{
    /// <summary>A percentage (CPU/GPU/RAM usage and activity), normally 0–100.</summary>
    Percent,

    /// <summary>A memory amount in bytes (RAM/VRAM used and total).</summary>
    Bytes,

    /// <summary>A data rate in bytes per second (disk and network throughput).</summary>
    BytesPerSecond,

    /// <summary>A temperature in degrees Celsius.</summary>
    Celsius,

    /// <summary>A fan speed in revolutions per minute.</summary>
    Rpm,
}

/// <summary>
/// One value of an application-level snapshot addressed by a stable sensor key (RF-063). A reading
/// is either available (<see cref="Value"/> is a finite number) or explicitly unavailable
/// (<see cref="Value"/> is <see langword="null"/>); values are never fabricated as zero.
/// </summary>
/// <param name="Value">The reading value, or <see langword="null"/> when the sensor is unavailable.</param>
/// <param name="Unit">The unit the value is expressed in.</param>
public readonly record struct SensorReading(double? Value, SensorUnit Unit)
{
    /// <summary>Gets a value indicating whether a real value is present.</summary>
    public bool IsAvailable => Value is { } value && double.IsFinite(value);
}
