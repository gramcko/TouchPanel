namespace TouchPanel.Monitoring;

/// <summary>
/// Default WPF-free <see cref="ISensorCatalog"/> (M5-RF-510). It always offers the documented
/// aggregate sensor keys and, when the injected <see cref="ISensorSnapshotSource"/> has produced a
/// snapshot, appends the per-core CPU and fan keys sized from that snapshot. With no snapshot (or no
/// source) it still returns the fixed aggregate list, so the picker is never empty (M5-RF-512).
/// </summary>
/// <remarks>
/// The catalog is creation/read driven only: it reads <see cref="ISensorSnapshotSource.Latest"/> on
/// demand and never subscribes, polls, or backgrounds work (M5-RNF-555).
/// </remarks>
public sealed class SensorCatalog : ISensorCatalog
{
    // M5-RF-510/M5-RF-511: the display name carries the stable key and the reading's unit so the
    // sensor picker never leaves the user guessing whether a key is a percentage or a temperature
    // (for example "CPU temp (temp.cpu) — °C" versus "CPU total (cpu.total) — %").
    private static readonly SensorOption[] AggregateOptions =
    [
        new(SensorKeys.CpuTotal, "CPU total (cpu.total) — %"),
        new(SensorKeys.GpuUsage, "GPU usage (gpu.usage) — %"),
        new(SensorKeys.GpuVramPercent, "GPU VRAM (gpu.vram.percent) — %"),
        new(SensorKeys.GpuVramUsed, "GPU VRAM used (gpu.vram.used) — bytes"),
        new(SensorKeys.GpuVramTotal, "GPU VRAM total (gpu.vram.total) — bytes"),
        new(SensorKeys.RamPercent, "RAM (ram.percent) — %"),
        new(SensorKeys.RamUsed, "RAM used (ram.used) — bytes"),
        new(SensorKeys.RamTotal, "RAM total (ram.total) — bytes"),
        new(SensorKeys.DiskActivity, "Disk activity (disk.activity) — %"),
        new(SensorKeys.DiskRead, "Disk read (disk.read) — B/s"),
        new(SensorKeys.DiskWrite, "Disk write (disk.write) — B/s"),
        new(SensorKeys.NetworkUp, "Network up (net.up) — B/s"),
        new(SensorKeys.NetworkDown, "Network down (net.down) — B/s"),
        new(SensorKeys.CpuTemperature, "CPU temp (temp.cpu) — °C"),
        new(SensorKeys.GpuTemperature, "GPU temp (temp.gpu) — °C"),
    ];

    private readonly ISensorSnapshotSource? _snapshotSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="SensorCatalog"/> class.
    /// </summary>
    /// <param name="snapshotSource">
    /// The snapshot boundary used to discover per-core/fan keys; <see langword="null"/> (or a source
    /// that has not published a snapshot yet) yields the fixed aggregate list.
    /// </param>
    public SensorCatalog(ISensorSnapshotSource? snapshotSource = null) => _snapshotSource = snapshotSource;

    /// <inheritdoc />
    public IReadOnlyList<SensorOption> GetSensorOptions()
    {
        var options = new List<SensorOption>(AggregateOptions.Length);
        options.AddRange(AggregateOptions);

        var snapshot = _snapshotSource?.Latest;
        if (snapshot is not null)
        {
            for (var core = 0; core < snapshot.CpuCorePercents.Length; core++)
            {
                var key = SensorKeys.CpuCore(core);
                options.Add(new SensorOption(key, $"CPU core {core} ({key}) — %"));
            }

            for (var fan = 0; fan < snapshot.FanSpeedsRpm.Length; fan++)
            {
                var key = SensorKeys.Fan(fan);
                options.Add(new SensorOption(key, $"Fan {fan} ({key}) — RPM"));
            }
        }

        return options;
    }

    /// <inheritdoc />
    public IReadOnlyList<SensorOption> GetSensorOptions(string? currentSensorKey)
    {
        var options = new List<SensorOption>(GetSensorOptions());

        if (!string.IsNullOrWhiteSpace(currentSensorKey)
            && !options.Exists(option => string.Equals(option.Key, currentSensorKey, StringComparison.OrdinalIgnoreCase)))
        {
            // M5-RF-512: never silently drop a hand-edited/hardware-gone key; show it as "current".
            options.Add(new SensorOption(currentSensorKey, $"{currentSensorKey} (current)"));
        }

        return options;
    }
}
