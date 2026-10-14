using System.Collections.Immutable;
using System.Globalization;

namespace TouchPanel.Monitoring;

/// <summary>
/// An immutable application-level telemetry sample (RF-062/RF-063). Every field is nullable and a
/// <see langword="null"/> value means "sensor unavailable" — never a fabricated zero. LibreHardwareMonitor
/// types never appear here; adapters translate hardware readings into this shape.
/// </summary>
/// <remarks>
/// Indexed collections use <see cref="ImmutableArray{T}"/> so the snapshot can be handed to the UI
/// thread without defensive copies. <see cref="CpuCorePercents"/> preserves core positions and
/// marks individual unavailable cores with <see langword="null"/>; <see cref="FanSpeedsRpm"/>
/// contains only fans that produced a finite reading.
/// </remarks>
public sealed record SensorSnapshot
{
    /// <summary>Gets the UTC instant the snapshot was captured.</summary>
    public DateTimeOffset CapturedAt { get; init; }

    /// <summary>Gets the total CPU usage in percent, or <see langword="null"/> when unavailable.</summary>
    public double? CpuTotalPercent { get; init; }

    /// <summary>Gets the per-core CPU usage in percent; core positions are preserved and unavailable cores are <see langword="null"/>.</summary>
    public ImmutableArray<double?> CpuCorePercents { get; init; } = ImmutableArray<double?>.Empty;

    /// <summary>Gets the GPU usage in percent, or <see langword="null"/> when unavailable.</summary>
    public double? GpuUsagePercent { get; init; }

    /// <summary>Gets the GPU memory in use in bytes, or <see langword="null"/> when unavailable.</summary>
    public double? VramUsedBytes { get; init; }

    /// <summary>Gets the total GPU memory in bytes, or <see langword="null"/> when unavailable.</summary>
    public double? VramTotalBytes { get; init; }

    /// <summary>
    /// Gets the VRAM usage in percent, derived from <see cref="VramUsedBytes"/> and
    /// <see cref="VramTotalBytes"/>, or <see langword="null"/> when either input is unavailable.
    /// </summary>
    public double? VramPercent => ComputePercent(VramUsedBytes, VramTotalBytes);

    /// <summary>Gets the system RAM in use in bytes, or <see langword="null"/> when unavailable.</summary>
    public double? RamUsedBytes { get; init; }

    /// <summary>Gets the total system RAM in bytes, or <see langword="null"/> when unavailable.</summary>
    public double? RamTotalBytes { get; init; }

    /// <summary>Gets the system RAM usage in percent, or <see langword="null"/> when unavailable.</summary>
    public double? RamPercent { get; init; }

    /// <summary>Gets the overall disk activity in percent, or <see langword="null"/> when unavailable.</summary>
    public double? DiskActivityPercent { get; init; }

    /// <summary>Gets the disk read throughput in bytes per second, or <see langword="null"/> when unavailable.</summary>
    public double? DiskReadBytesPerSecond { get; init; }

    /// <summary>Gets the disk write throughput in bytes per second, or <see langword="null"/> when unavailable.</summary>
    public double? DiskWriteBytesPerSecond { get; init; }

    /// <summary>Gets the network upload throughput in bytes per second, or <see langword="null"/> when unavailable.</summary>
    public double? NetworkUpBytesPerSecond { get; init; }

    /// <summary>Gets the network download throughput in bytes per second, or <see langword="null"/> when unavailable.</summary>
    public double? NetworkDownBytesPerSecond { get; init; }

    /// <summary>Gets the CPU temperature in degrees Celsius, or <see langword="null"/> when unavailable.</summary>
    public double? CpuTemperatureCelsius { get; init; }

    /// <summary>Gets the GPU temperature in degrees Celsius, or <see langword="null"/> when unavailable.</summary>
    public double? GpuTemperatureCelsius { get; init; }

    /// <summary>Gets the fan speeds in RPM; empty when no fan reported a value.</summary>
    public ImmutableArray<double> FanSpeedsRpm { get; init; } = ImmutableArray<double>.Empty;

    /// <summary>
    /// Creates a snapshot whose every field is explicitly unavailable. Used when the hardware
    /// library cannot initialize or a poll fails (RF-065); no value is fabricated as zero.
    /// </summary>
    /// <param name="capturedAt">The capture instant the caller should stamp.</param>
    /// <returns>A fully unavailable snapshot.</returns>
    public static SensorSnapshot CreateUnavailable(DateTimeOffset capturedAt) =>
        new() { CapturedAt = capturedAt };

    /// <summary>
    /// Resolves one stable application-level sensor key (see <see cref="SensorKeys"/>) to its
    /// value and unit. Known keys always succeed, reporting an unavailable reading
    /// (<see cref="SensorReading.Value"/> is <see langword="null"/>) when the machine does not
    /// expose the sensor; unknown keys fail and let the caller surface an unsupported sensor.
    /// </summary>
    /// <param name="sensorKey">The configured sensor key.</param>
    /// <param name="reading">The resolved reading when the key is known.</param>
    /// <returns><see langword="true"/> when the key is one of the documented keys.</returns>
    public bool TryGetReading(string sensorKey, out SensorReading reading)
    {
        reading = default;
        if (string.IsNullOrWhiteSpace(sensorKey))
        {
            return false;
        }

        var key = sensorKey.Trim();

        switch (key.ToLowerInvariant())
        {
            case SensorKeys.CpuTotal:
                reading = new SensorReading(CpuTotalPercent, SensorUnit.Percent);
                return true;
            case SensorKeys.GpuUsage:
                reading = new SensorReading(GpuUsagePercent, SensorUnit.Percent);
                return true;
            case SensorKeys.GpuVramUsed:
                reading = new SensorReading(VramUsedBytes, SensorUnit.Bytes);
                return true;
            case SensorKeys.GpuVramTotal:
                reading = new SensorReading(VramTotalBytes, SensorUnit.Bytes);
                return true;
            case SensorKeys.GpuVramPercent:
                reading = new SensorReading(VramPercent, SensorUnit.Percent);
                return true;
            case SensorKeys.RamUsed:
                reading = new SensorReading(RamUsedBytes, SensorUnit.Bytes);
                return true;
            case SensorKeys.RamTotal:
                reading = new SensorReading(RamTotalBytes, SensorUnit.Bytes);
                return true;
            case SensorKeys.RamPercent:
                reading = new SensorReading(RamPercent, SensorUnit.Percent);
                return true;
            case SensorKeys.DiskActivity:
                reading = new SensorReading(DiskActivityPercent, SensorUnit.Percent);
                return true;
            case SensorKeys.DiskRead:
                reading = new SensorReading(DiskReadBytesPerSecond, SensorUnit.BytesPerSecond);
                return true;
            case SensorKeys.DiskWrite:
                reading = new SensorReading(DiskWriteBytesPerSecond, SensorUnit.BytesPerSecond);
                return true;
            case SensorKeys.NetworkUp:
                reading = new SensorReading(NetworkUpBytesPerSecond, SensorUnit.BytesPerSecond);
                return true;
            case SensorKeys.NetworkDown:
                reading = new SensorReading(NetworkDownBytesPerSecond, SensorUnit.BytesPerSecond);
                return true;
            case SensorKeys.CpuTemperature:
                reading = new SensorReading(CpuTemperatureCelsius, SensorUnit.Celsius);
                return true;
            case SensorKeys.GpuTemperature:
                reading = new SensorReading(GpuTemperatureCelsius, SensorUnit.Celsius);
                return true;
        }

        if (TryParseIndex(key, SensorKeys.CpuCorePrefix, out var coreIndex))
        {
            var value = coreIndex < CpuCorePercents.Length ? CpuCorePercents[coreIndex] : null;
            reading = new SensorReading(value, SensorUnit.Percent);
            return true;
        }

        if (TryParseIndex(key, SensorKeys.FanPrefix, out var fanIndex))
        {
            double? value = fanIndex < FanSpeedsRpm.Length ? FanSpeedsRpm[fanIndex] : null;
            reading = new SensorReading(value, SensorUnit.Rpm);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Computes a clamped percentage from a used/total pair; returns <see langword="null"/> when
    /// either input is missing or the total is not positive.
    /// </summary>
    internal static double? ComputePercent(double? used, double? total)
    {
        if (used is not { } usedValue || total is not { } totalValue || totalValue <= 0)
        {
            return null;
        }

        return Math.Clamp(usedValue / totalValue * 100d, 0d, 100d);
    }

    private static bool TryParseIndex(string key, string prefix, out int index)
    {
        index = -1;
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = key.AsSpan(prefix.Length);

        // Reject signs, whitespace, and separator characters: only plain digits are valid indices.
        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
}
