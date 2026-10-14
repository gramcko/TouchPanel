using System.Collections.Immutable;

namespace TouchPanel.Monitoring;

/// <summary>
/// Raw values harvested from a hardware backend, already expressed in application units (percent,
/// bytes, bytes/second, Celsius, RPM) before sanitization. This is the input to
/// <see cref="SensorSnapshotAggregator"/> and lets the mapping/sanitization logic be unit-tested
/// with synthetic values and without any hardware (RF-063/RNF-006).
/// </summary>
internal sealed record SensorAggregationInput
{
    /// <summary>Gets the total CPU usage in percent.</summary>
    public double? CpuTotalPercent { get; init; }

    /// <summary>Gets the per-core CPU usage in percent; positions are preserved.</summary>
    public IReadOnlyList<double>? CpuCorePercents { get; init; }

    /// <summary>Gets the GPU usage in percent.</summary>
    public double? GpuUsagePercent { get; init; }

    /// <summary>Gets the GPU memory in use in bytes.</summary>
    public double? VramUsedBytes { get; init; }

    /// <summary>Gets the total GPU memory in bytes.</summary>
    public double? VramTotalBytes { get; init; }

    /// <summary>Gets the system RAM in use in bytes.</summary>
    public double? RamUsedBytes { get; init; }

    /// <summary>Gets the total system RAM in bytes.</summary>
    public double? RamTotalBytes { get; init; }

    /// <summary>Gets a fallback RAM percentage used only when used/total cannot produce one.</summary>
    public double? RamPercent { get; init; }

    /// <summary>Gets the overall disk activity in percent.</summary>
    public double? DiskActivityPercent { get; init; }

    /// <summary>Gets the disk read throughput in bytes per second.</summary>
    public double? DiskReadBytesPerSecond { get; init; }

    /// <summary>Gets the disk write throughput in bytes per second.</summary>
    public double? DiskWriteBytesPerSecond { get; init; }

    /// <summary>Gets the network upload throughput in bytes per second.</summary>
    public double? NetworkUpBytesPerSecond { get; init; }

    /// <summary>Gets the network download throughput in bytes per second.</summary>
    public double? NetworkDownBytesPerSecond { get; init; }

    /// <summary>Gets the CPU temperature in degrees Celsius.</summary>
    public double? CpuTemperatureCelsius { get; init; }

    /// <summary>Gets the GPU temperature in degrees Celsius.</summary>
    public double? GpuTemperatureCelsius { get; init; }

    /// <summary>Gets the fan speeds in RPM.</summary>
    public IReadOnlyList<double>? FanSpeedsRpm { get; init; }
}

/// <summary>
/// Pure projection from raw backend values to an immutable <see cref="SensorSnapshot"/>
/// (RF-063/RF-065): NaN/infinite/negative-invalid readings become <see langword="null"/>
/// ("unavailable", never a fabricated zero), valid percentages above 100 are clamped to 100,
/// per-core positions are preserved with <see langword="null"/> slots for missing cores, and
/// RAM/VRAM percentages are derived from used/total when possible.
/// </summary>
internal static class SensorSnapshotAggregator
{
    /// <summary>
    /// Sanitizes raw input and stamps the snapshot. The method is total: every invalid value maps
    /// to an explicit unavailable marker and no exception path exists.
    /// </summary>
    /// <param name="input">The raw values in application units; must not be <see langword="null"/>.</param>
    /// <param name="capturedAt">The capture instant to stamp.</param>
    /// <returns>The immutable, sanitized snapshot.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is <see langword="null"/>.</exception>
    public static SensorSnapshot Create(SensorAggregationInput input, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(input);

        var ramUsed = SanitizeNonNegative(input.RamUsedBytes);
        var ramTotal = SanitizeNonNegative(input.RamTotalBytes);

        return new SensorSnapshot
        {
            CapturedAt = capturedAt,
            CpuTotalPercent = SanitizePercent(input.CpuTotalPercent),
            CpuCorePercents = SanitizeCorePercents(input.CpuCorePercents),
            GpuUsagePercent = SanitizePercent(input.GpuUsagePercent),
            VramUsedBytes = SanitizeNonNegative(input.VramUsedBytes),
            VramTotalBytes = SanitizeNonNegative(input.VramTotalBytes),
            RamUsedBytes = ramUsed,
            RamTotalBytes = ramTotal,
            RamPercent = SensorSnapshot.ComputePercent(ramUsed, ramTotal) ?? SanitizePercent(input.RamPercent),
            DiskActivityPercent = SanitizePercent(input.DiskActivityPercent),
            DiskReadBytesPerSecond = SanitizeNonNegative(input.DiskReadBytesPerSecond),
            DiskWriteBytesPerSecond = SanitizeNonNegative(input.DiskWriteBytesPerSecond),
            NetworkUpBytesPerSecond = SanitizeNonNegative(input.NetworkUpBytesPerSecond),
            NetworkDownBytesPerSecond = SanitizeNonNegative(input.NetworkDownBytesPerSecond),
            CpuTemperatureCelsius = SanitizeFinite(input.CpuTemperatureCelsius),
            GpuTemperatureCelsius = SanitizeFinite(input.GpuTemperatureCelsius),
            FanSpeedsRpm = SanitizeFans(input.FanSpeedsRpm),
        };
    }

    /// <summary>
    /// Maps NaN/infinite and negative values to unavailable and clamps a valid percentage above
    /// 100 down to 100. A negative reading is invalid for a percentage and is never fabricated
    /// as 0 (RF-063).
    /// </summary>
    internal static double? SanitizePercent(double? value) =>
        SanitizeFinite(value) is { } finite && finite >= 0 ? Math.Min(finite, 100d) : null;

    /// <summary>Maps NaN/infinite and negative values to unavailable (negative amounts are invalid).</summary>
    internal static double? SanitizeNonNegative(double? value) =>
        SanitizeFinite(value) is { } finite && finite >= 0 ? finite : null;

    /// <summary>Maps NaN/infinite values to unavailable.</summary>
    internal static double? SanitizeFinite(double? value) =>
        value is { } value2 && double.IsFinite(value2) ? value2 : null;

    private static ImmutableArray<double?> SanitizeCorePercents(IReadOnlyList<double>? cores)
    {
        if (cores is null || cores.Count == 0)
        {
            return ImmutableArray<double?>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<double?>(cores.Count);
        foreach (var core in cores)
        {
            // Position-preserving: an unavailable core stays a null slot so cpu.core.<n> keys
            // remain stable and no core is silently reported as 0%.
            builder.Add(SanitizePercent(core));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<double> SanitizeFans(IReadOnlyList<double>? fans)
    {
        if (fans is null || fans.Count == 0)
        {
            return ImmutableArray<double>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<double>(fans.Count);
        foreach (var fan in fans)
        {
            // Fans have no stable index to preserve, so unavailable entries are dropped entirely.
            if (SanitizeNonNegative(fan) is { } value)
            {
                builder.Add(value);
            }
        }

        return builder.ToImmutable();
    }
}
