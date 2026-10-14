namespace TouchPanel.Monitoring;

/// <summary>
/// The subset of hardware sensor kinds the adapter maps into application values. An application
/// level mirror of the hardware library's sensor types so the selection helpers stay free of
/// LibreHardwareMonitor types (RF-062) and can be unit-tested without hardware.
/// </summary>
internal enum SensorKind
{
    /// <summary>A load/usage percentage (CPU total, GPU core, memory, disk activity).</summary>
    Load,

    /// <summary>A temperature in degrees Celsius.</summary>
    Temperature,

    /// <summary>A fan speed in RPM.</summary>
    Fan,

    /// <summary>A memory amount reported in gigabytes (LHM <c>Data</c> convention).</summary>
    Data,

    /// <summary>A memory amount reported in megabytes (LHM <c>SmallData</c> convention).</summary>
    SmallData,

    /// <summary>A throughput rate (storage MB/s, network KB/s).</summary>
    Throughput,

    /// <summary>A clock frequency in megahertz.</summary>
    Clock,

    /// <summary>A voltage in volts.</summary>
    Voltage,

    /// <summary>A current in amperes.</summary>
    Current,

    /// <summary>A power draw in watts.</summary>
    Power,
}

/// <summary>
/// One hardware sensor projected out of the library: its name, application-level kind, and raw
/// value. A <see langword="null"/> value means the sensor reported no reading.
/// </summary>
/// <param name="Name">The sensor name as reported by the hardware library.</param>
/// <param name="Kind">The application-level sensor kind.</param>
/// <param name="Value">The raw sensor value, or <see langword="null"/> when unreadable.</param>
internal readonly record struct SensorCandidate(string Name, SensorKind Kind, double? Value);

/// <summary>
/// Pure sensor selection rules extracted from <c>LibreHardwareMonitorAdapter</c> (RF-062):
/// case-insensitive exact-name matching in priority order, first finite-value fallback, prefix
/// collection, and the GPU-memory SmallData/Data fallback. No hardware-library types appear in any
/// signature, so the mapping behavior is unit-testable without hardware.
/// </summary>
internal static class SensorSelector
{
    /// <summary>
    /// Finds the value of the first sensor matching one of the names in priority order within the
    /// given kind. Non-finite or missing values are skipped so a dead sensor falls through to the
    /// next candidate.
    /// </summary>
    /// <param name="sensors">The projected sensors in hardware order.</param>
    /// <param name="kind">The sensor kind to match.</param>
    /// <param name="names">The accepted sensor names, highest priority first.</param>
    /// <returns>The first finite matching value, or <see langword="null"/> when none matches.</returns>
    public static double? FindValue(
        IReadOnlyList<SensorCandidate> sensors,
        SensorKind kind,
        params string[] names)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(names);

        foreach (var name in names)
        {
            foreach (var sensor in sensors)
            {
                if (sensor.Kind == kind
                    && string.Equals(sensor.Name, name, StringComparison.OrdinalIgnoreCase)
                    && sensor.Value is { } value
                    && double.IsFinite(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the value of the first sensor of the given kind whose name contains one of the given
    /// fragments (case-insensitively), in fragment priority order. This is the tolerant variant of
    /// <see cref="FindValue"/> used where the hardware library's exact sensor name is neither stable
    /// nor documented (for example network throughput, whose sensors may be prefixed with an adapter
    /// name). Non-finite or missing values are skipped so a dead sensor falls through.
    /// </summary>
    /// <param name="sensors">The projected sensors in hardware order.</param>
    /// <param name="kind">The sensor kind to match.</param>
    /// <param name="nameFragments">The accepted name fragments, highest priority first.</param>
    /// <returns>The first finite matching value, or <see langword="null"/> when none matches.</returns>
    public static double? FindValueContaining(
        IReadOnlyList<SensorCandidate> sensors,
        SensorKind kind,
        params string[] nameFragments)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(nameFragments);

        foreach (var fragment in nameFragments)
        {
            if (string.IsNullOrEmpty(fragment))
            {
                continue;
            }

            foreach (var sensor in sensors)
            {
                if (sensor.Kind == kind
                    && sensor.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                    && sensor.Value is { } value
                    && double.IsFinite(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the value the adapter should keep when several hardware nodes each expose the same
    /// metric (for example several network adapters): the first finite reading wins, but a later
    /// node reporting actual activity replaces an idle zero, so a busy adapter is never masked by an
    /// idle one that happens to be enumerated first. A <see langword="null"/> candidate leaves the
    /// current value untouched (falls through to the next node), and a zero candidate is kept when
    /// no positive reading has been seen so far.
    /// </summary>
    /// <param name="current">The value already resolved from earlier nodes, or <see langword="null"/>.</param>
    /// <param name="candidate">The value resolved from the current node, or <see langword="null"/>.</param>
    /// <returns>The preferred value.</returns>
    public static double? PreferActiveValue(double? current, double? candidate)
    {
        if (candidate is not { } value)
        {
            return current;
        }

        if (current is not { } currentValue)
        {
            return value;
        }

        return currentValue <= 0d && value > 0d ? value : current;
    }

    /// <summary>
    /// Finds the first finite value of the given kind in hardware order; the fallback used when no
    /// preferred sensor name is present.
    /// </summary>
    /// <param name="sensors">The projected sensors in hardware order.</param>
    /// <param name="kind">The sensor kind to match.</param>
    /// <returns>The first finite matching value, or <see langword="null"/> when none exists.</returns>
    public static double? FindFirstValue(IReadOnlyList<SensorCandidate> sensors, SensorKind kind)
    {
        ArgumentNullException.ThrowIfNull(sensors);

        foreach (var sensor in sensors)
        {
            if (sensor.Kind == kind
                && sensor.Value is { } value
                && double.IsFinite(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Collects every finite value of the given kind in hardware order (for example all fan
    /// sensors spread across the motherboard and GPU hardware).
    /// </summary>
    /// <param name="sensors">The projected sensors in hardware order.</param>
    /// <param name="kind">The sensor kind to collect.</param>
    /// <returns>The finite values in hardware order; empty when none exists.</returns>
    public static IReadOnlyList<double> FindAllValues(IReadOnlyList<SensorCandidate> sensors, SensorKind kind)
    {
        ArgumentNullException.ThrowIfNull(sensors);

        var values = new List<double>();
        foreach (var sensor in sensors)
        {
            if (sensor.Kind == kind
                && sensor.Value is { } value
                && double.IsFinite(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// Collects every finite value of the given kind whose name starts with the prefix (for
    /// example the per-core <c>CPU Core #</c> load sensors), in hardware order.
    /// </summary>
    /// <param name="sensors">The projected sensors in hardware order.</param>
    /// <param name="kind">The sensor kind to match.</param>
    /// <param name="namePrefix">The case-insensitive name prefix.</param>
    /// <returns>The finite matching values in hardware order; empty when none exists.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="namePrefix"/> is empty.</exception>
    public static IReadOnlyList<double> FindValuesByNamePrefix(
        IReadOnlyList<SensorCandidate> sensors,
        SensorKind kind,
        string namePrefix)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentException.ThrowIfNullOrWhiteSpace(namePrefix);

        var values = new List<double>();
        foreach (var sensor in sensors)
        {
            if (sensor.Kind == kind
                && sensor.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)
                && sensor.Value is { } value
                && double.IsFinite(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// Resolves GPU memory to bytes with the adapter's fallback ordering: names are tried in
    /// priority order and, within one name, the SmallData (megabytes) convention is preferred over
    /// the Data (gigabytes) convention.
    /// </summary>
    /// <param name="sensors">The projected sensors in hardware order.</param>
    /// <param name="names">The accepted sensor names, highest priority first.</param>
    /// <returns>The resolved memory in bytes, or <see langword="null"/> when none matches.</returns>
    public static double? FindGpuMemoryBytes(IReadOnlyList<SensorCandidate> sensors, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(names);

        foreach (var name in names)
        {
            if (FindValue(sensors, SensorKind.SmallData, name) is { } megabytes)
            {
                return SensorValueConverter.ToApplicationValue(megabytes, SensorValueUnit.Megabytes);
            }

            if (FindValue(sensors, SensorKind.Data, name) is { } gigabytes)
            {
                return SensorValueConverter.ToApplicationValue(gigabytes, SensorValueUnit.Gigabytes);
            }
        }

        return null;
    }
}
