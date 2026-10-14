using LibreHardwareMonitor.Hardware;
using Serilog;

namespace TouchPanel.Monitoring;

/// <summary>
/// The LibreHardwareMonitor adapter (DD-10/RF-062): the single file in the application that
/// references <c>LibreHardwareMonitorLib</c>. It lazily opens the library on the polling thread,
/// translates hardware sensors into a <see cref="SensorSnapshot"/> with explicit per-field
/// availability, catches every initialization/read failure (throttled logging via
/// <see cref="TimeProvider"/>, RF-065), and never requests elevation (RF-068/D-11).
/// </summary>
/// <remarks>
/// <para><b>Units.</b> Values are converted from LibreHardwareMonitor's native conventions to the
/// application units: RAM/VRAM to bytes, storage throughput from MB/s, network throughput from
/// bytes/second (identity), temperatures to °C, and fan speeds to RPM.</para>
/// <para><b>Lazy init.</b> <see cref="Computer.Open"/> runs on the first <see cref="Poll"/> call,
/// which the <see cref="MonitoringService"/> always executes on its background task. If the open
/// fails, the adapter reports unavailable snapshots; the monitoring loop drives periodic
/// re-initialization through <see cref="TryReinitialize"/> on a bounded throttle (RF-070/M2-RF-701).</para>
/// </remarks>
internal sealed class LibreHardwareMonitorAdapter : ISensorSource, ISensorSourceHealth, IDisposable
{
    private static readonly string[] CpuTemperatureNames =
    [
        "CPU Package",
        "Package",
        "Core Average",
        "CPU Core Average",
        "Core Max",
        "CPU Core Max",
        "CPU Cores",
        "Core (Tctl/Tdie)",
        "Core (Tctl)",
        "Core (Tdie)",
        "CPU (Tctl/Tdie)",
        "Tctl",
        "Tdie",
        "CPU CCD1 (Tdie)",
        "CPU CCD2 (Tdie)",
    ];

    private static readonly string[] GpuTemperatureNames =
    [
        "GPU Core",
        "GPU Hot Spot",
    ];

    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ThrottledFailureLogger _failureLog;
    private readonly object _gate = new();
    private Computer? _computer;
    private string? _lastFailureReason;
    private bool _openAttempted;
    private bool _cpuTemperatureUnavailableLogged;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibreHardwareMonitorAdapter"/> class. No
    /// library call happens here; the hardware tree opens lazily on the first poll.
    /// </summary>
    /// <param name="logger">Logger for lifecycle and throttled failure entries; defaults to the global Serilog logger.</param>
    /// <param name="timeProvider">Clock for failure-log throttling; defaults to the system clock.</param>
    /// <param name="failureLogThrottle">The failure-log throttle window; defaults to one minute.</param>
    public LibreHardwareMonitorAdapter(
        ILogger? logger = null,
        TimeProvider? timeProvider = null,
        TimeSpan? failureLogThrottle = null)
    {
        _logger = logger ?? Log.Logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _failureLog = new ThrottledFailureLogger(_logger, _timeProvider, failureLogThrottle);
    }

    /// <inheritdoc />
    public string? LastFailureReason
    {
        get
        {
            lock (_gate)
            {
                return _lastFailureReason;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Must be called from the background polling loop; the lazy <see cref="Computer.Open"/> and
    /// every sensor read happen on the calling thread. The method never throws.
    /// </remarks>
    public SensorSnapshot Poll()
    {
        lock (_gate)
        {
            var capturedAt = _timeProvider.GetUtcNow();
            if (_disposed)
            {
                return SensorSnapshot.CreateUnavailable(capturedAt);
            }

            try
            {
                var computer = EnsureOpen();
                if (computer is null)
                {
                    return SensorSnapshot.CreateUnavailable(capturedAt);
                }

                var snapshot = Capture(computer, capturedAt);

                // The backend was reachable and read successfully; individual missing sensors are
                // not failures (RF-065/RF-068). The monitoring service turns this into the
                // RF-012 health transition.
                _lastFailureReason = null;
                return snapshot;
            }
            catch (Exception ex)
            {
                _lastFailureReason = "reading LibreHardwareMonitor sensors failed";
                _failureLog.Report(ex, "Monitoring: reading LibreHardwareMonitor sensors failed");
                return SensorSnapshot.CreateUnavailable(capturedAt);
            }
        }
    }

    /// <summary>
    /// Re-opens the hardware backend after a failure (RF-070/M2-RF-701). Called by the monitoring
    /// loop through its throttled <see cref="BackendReinitializer"/> on the background polling
    /// thread. Any previously opened handle is closed first so a failed or partially initialized
    /// backend cannot leak, then <see cref="Computer.Open"/> is attempted again. Failure is logged
    /// at the throttled rate and never throws (RF-065); no elevation is ever requested (RF-068).
    /// </summary>
    /// <returns><see langword="true"/> when the backend is reachable again; otherwise <see langword="false"/>.</returns>
    public bool TryReinitialize()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            CloseComputer();
            _openAttempted = false;

            var computer = EnsureOpen();
            if (computer is null)
            {
                return false;
            }

            // The next successful poll clears this too; clearing now lets the monitoring service
            // restore the subsystem immediately on a successful re-open (M2-RF-702).
            _lastFailureReason = null;
            return true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var hadComputer = _computer is not null;

            // RF-066: cleanly close the hardware library (drivers/streams).
            CloseComputer();
            if (hadComputer)
            {
                _logger.Information("Monitoring: LibreHardwareMonitor closed");
            }
        }
    }

    /// <summary>
    /// Closes and releases the currently opened library handle, if any. Failures are logged and
    /// contained so teardown and re-initialization never throw (RF-065/RF-066).
    /// </summary>
    private void CloseComputer()
    {
        var computer = _computer;
        _computer = null;
        if (computer is null)
        {
            return;
        }

        try
        {
            computer.Close();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Monitoring: closing LibreHardwareMonitor failed");
        }
    }

    /// <summary>
    /// Opens the hardware tree on the calling (background) thread, at most once per successful
    /// open: a failure is logged at a throttled rate and leaves the adapter reporting unavailable
    /// values until <see cref="TryReinitialize"/> is invoked on the monitoring loop's bounded
    /// cadence (RF-065/RF-070).
    /// </summary>
    /// <returns>The opened <see cref="Computer"/>, or <see langword="null"/> when initialization failed.</returns>
    private Computer? EnsureOpen()
    {
        if (_openAttempted)
        {
            return _computer;
        }

        _openAttempted = true;
        try
        {
            // RF-068/D-11: no elevation is requested anywhere on this path. Sensors that require
            // administrator rights or kernel drivers are simply missing from the tree and are
            // reported as unavailable, never as an application error.
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
            };

            computer.Open();
            _computer = computer;

            _logger.Information(
                "Monitoring: LibreHardwareMonitor initialized. Hardware={HardwareCount}",
                computer.Hardware.Count);
        }
        catch (Exception ex)
        {
            _computer = null;
            _lastFailureReason = "LibreHardwareMonitor initialization failed";
            _failureLog.Report(
                ex,
                "Monitoring: LibreHardwareMonitor initialization failed; sensors stay unavailable");
        }

        return _computer;
    }

    private SensorSnapshot Capture(Computer computer, DateTimeOffset capturedAt)
    {
        double? cpuTotal = null;
        double? cpuTemperature = null;
        var cores = new List<double>();
        double? gpuUsage = null;
        double? gpuTemperature = null;
        double? vramUsedBytes = null;
        double? vramTotalBytes = null;
        double? ramUsedBytes = null;
        double? ramAvailableBytes = null;
        double? ramPercent = null;
        double? diskActivity = null;
        double? diskReadBytesPerSecond = null;
        double? diskWriteBytesPerSecond = null;
        double? networkUpBytesPerSecond = null;
        double? networkDownBytesPerSecond = null;
        var sawCpuHardware = false;
        var fans = new List<double>();

        foreach (var hardware in computer.Hardware)
        {
            UpdateHardwareSafely(hardware);

            // Project the library sensors once; name selection and unit conversion then run through
            // the pure, hardware-free helpers (RF-062).
            var sensors = ProjectSensors(hardware);

            switch (hardware.HardwareType)
            {
                case HardwareType.Cpu:
                    sawCpuHardware = true;
                    cpuTotal ??= SensorValueConverter.ToApplicationValue(
                        SensorSelector.FindValue(sensors, SensorKind.Load, "CPU Total"),
                        SensorValueUnit.Percent);
                    cpuTemperature ??= SensorValueConverter.ToApplicationValue(
                        SensorSelector.FindValue(sensors, SensorKind.Temperature, CpuTemperatureNames)
                            ?? SensorSelector.FindFirstValue(sensors, SensorKind.Temperature),
                        SensorValueUnit.Celsius);
                    cores.AddRange(SensorSelector.FindValuesByNamePrefix(sensors, SensorKind.Load, "CPU Core #"));
                    break;

                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    gpuUsage ??= SensorValueConverter.ToApplicationValue(
                        SensorSelector.FindValue(sensors, SensorKind.Load, "GPU Core"),
                        SensorValueUnit.Percent);
                    gpuTemperature ??= SensorValueConverter.ToApplicationValue(
                        SensorSelector.FindValue(sensors, SensorKind.Temperature, GpuTemperatureNames)
                            ?? SensorSelector.FindFirstValue(sensors, SensorKind.Temperature),
                        SensorValueUnit.Celsius);

                    // Keep used/total from the same GPU so the derived percentage stays coherent.
                    if (vramUsedBytes is null && vramTotalBytes is null)
                    {
                        vramUsedBytes = SensorSelector.FindGpuMemoryBytes(sensors, "GPU Memory Used", "D3D Dedicated Memory Used");
                        vramTotalBytes = SensorSelector.FindGpuMemoryBytes(sensors, "GPU Memory Total", "D3D Dedicated Memory Total");
                    }

                    break;

                case HardwareType.Memory:
                    if (ramUsedBytes is null)
                    {
                        ramUsedBytes = SensorValueConverter.ToApplicationValue(
                            SensorSelector.FindValue(sensors, SensorKind.Data, "Memory Used", "Physical Memory Used"),
                            SensorValueUnit.Gigabytes);
                        ramAvailableBytes = SensorValueConverter.ToApplicationValue(
                            SensorSelector.FindValue(sensors, SensorKind.Data, "Memory Available", "Physical Memory Available"),
                            SensorValueUnit.Gigabytes);
                    }

                    ramPercent ??= SensorValueConverter.ToApplicationValue(
                        SensorSelector.FindValue(sensors, SensorKind.Load, "Memory"),
                        SensorValueUnit.Percent);
                    break;

                case HardwareType.Storage:
                    diskActivity ??= SensorValueConverter.ToApplicationValue(
                        SensorSelector.FindValue(sensors, SensorKind.Load, "Total Activity"),
                        SensorValueUnit.Percent);
                    diskReadBytesPerSecond ??= SensorValueConverter.ToApplicationValue(
                        SensorSelector.FindValue(sensors, SensorKind.Throughput, "Read Rate"),
                        SensorValueUnit.MegabytesPerSecond);
                    diskWriteBytesPerSecond ??= SensorValueConverter.ToApplicationValue(
                        SensorSelector.FindValue(sensors, SensorKind.Throughput, "Write Rate"),
                        SensorValueUnit.MegabytesPerSecond);
                    break;

                case HardwareType.Network:
                    // LHM 0.9.6 exposes network throughput as "Download Speed"/"Upload Speed"
                    // (SensorType.Throughput) already in bytes/second; the adapter's tolerant
                    // name match tolerates adapter prefixes, and the non-zero preference lets a
                    // busy adapter win over an idle one enumerated first.
                    networkDownBytesPerSecond = SensorSelector.PreferActiveValue(
                        networkDownBytesPerSecond,
                        SensorValueConverter.ToApplicationValue(
                            SensorSelector.FindValueContaining(sensors, SensorKind.Throughput, "download"),
                            SensorValueUnit.BytesPerSecond));
                    networkUpBytesPerSecond = SensorSelector.PreferActiveValue(
                        networkUpBytesPerSecond,
                        SensorValueConverter.ToApplicationValue(
                            SensorSelector.FindValueContaining(sensors, SensorKind.Throughput, "upload"),
                            SensorValueUnit.BytesPerSecond));
                    break;
            }

            // Fans live under the motherboard and/or GPU hardware; collect them wherever present.
            fans.AddRange(SensorSelector.FindAllValues(sensors, SensorKind.Fan));
        }

        // RF-068: no elevation is requested, so a CPU whose temperature needs a kernel driver simply
        // has no temperature sensor. Report the reason once per session instead of leaving the user
        // guessing why the gauge shows "Unavailable" (never a fabricated zero).
        if (sawCpuHardware && cpuTemperature is null && !_cpuTemperatureUnavailableLogged)
        {
            _cpuTemperatureUnavailableLogged = true;
            _logger.Information(
                "Monitoring: CPU temperature is unavailable; LibreHardwareMonitor exposes it only when the CPU driver can be loaded (no elevation is requested, RF-068)");
        }

        var ramTotalBytes = ramUsedBytes is not null && ramAvailableBytes is not null
            ? ramUsedBytes + ramAvailableBytes
            : null;

        var input = new SensorAggregationInput
        {
            CpuTotalPercent = cpuTotal,
            CpuCorePercents = cores,
            CpuTemperatureCelsius = cpuTemperature,
            GpuUsagePercent = gpuUsage,
            GpuTemperatureCelsius = gpuTemperature,
            VramUsedBytes = vramUsedBytes,
            VramTotalBytes = vramTotalBytes,
            RamUsedBytes = ramUsedBytes,
            RamTotalBytes = ramTotalBytes,
            RamPercent = ramPercent,
            DiskActivityPercent = diskActivity,
            DiskReadBytesPerSecond = diskReadBytesPerSecond,
            DiskWriteBytesPerSecond = diskWriteBytesPerSecond,
            NetworkUpBytesPerSecond = networkUpBytesPerSecond,
            NetworkDownBytesPerSecond = networkDownBytesPerSecond,
            FanSpeedsRpm = fans,
        };

        return SensorSnapshotAggregator.Create(input, capturedAt);
    }

    /// <summary>
    /// Updates one hardware tree depth-first, isolating per-hardware failures so a broken GPU or
    /// embedded controller cannot blank unrelated sensors (RF-065).
    /// </summary>
    private void UpdateHardwareSafely(IHardware hardware)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception ex)
        {
            _failureLog.Report(ex, "Monitoring: updating hardware {HardwareName} failed", hardware.Name);
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            try
            {
                subHardware.Update();
            }
            catch (Exception ex)
            {
                _failureLog.Report(ex, "Monitoring: updating hardware {HardwareName} failed", subHardware.Name);
            }
        }
    }

    /// <summary>
    /// Projects the library sensors of one hardware node into library-free candidates (RF-062);
    /// unmapped sensor types (for example fans' control sensors) are ignored.
    /// </summary>
    /// <param name="hardware">The hardware node whose sensors are projected.</param>
    /// <returns>The candidates in hardware order.</returns>
    private static List<SensorCandidate> ProjectSensors(IHardware hardware)
    {
        var sensors = new List<SensorCandidate>(hardware.Sensors.Length);
        foreach (var sensor in hardware.Sensors)
        {
            if (TryMapSensorKind(sensor.SensorType, out var kind))
            {
                sensors.Add(new SensorCandidate(sensor.Name, kind, sensor.Value));
            }
        }

        return sensors;
    }

    /// <summary>Maps a hardware-library sensor type to the application-level sensor kind.</summary>
    /// <param name="sensorType">The library sensor type.</param>
    /// <param name="kind">The mapped application kind when the type is one the adapter consumes.</param>
    /// <returns><see langword="true"/> when the type participates in the snapshot mapping.</returns>
    private static bool TryMapSensorKind(SensorType sensorType, out SensorKind kind)
    {
        switch (sensorType)
        {
            case SensorType.Load:
                kind = SensorKind.Load;
                return true;
            case SensorType.Temperature:
                kind = SensorKind.Temperature;
                return true;
            case SensorType.Fan:
                kind = SensorKind.Fan;
                return true;
            case SensorType.Data:
                kind = SensorKind.Data;
                return true;
            case SensorType.SmallData:
                kind = SensorKind.SmallData;
                return true;
            case SensorType.Throughput:
                kind = SensorKind.Throughput;
                return true;
            case SensorType.Clock:
                kind = SensorKind.Clock;
                return true;
            case SensorType.Voltage:
                kind = SensorKind.Voltage;
                return true;
            case SensorType.Current:
                kind = SensorKind.Current;
                return true;
            case SensorType.Power:
                kind = SensorKind.Power;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
