using System.Globalization;

namespace TouchPanel.Monitoring;

/// <summary>
/// The stable application-level sensor keys a Gauge's <c>sensor</c> configuration string resolves
/// against (RF-034/RF-063). Keys are independent of the hardware backend; the mapping from keys to
/// snapshot fields lives in <see cref="SensorSnapshot.TryGetReading(string, out SensorReading)"/>.
/// </summary>
/// <remarks>
/// Key grammar: fixed keys for aggregate metrics, plus indexed families for per-core CPU usage
/// (<c>cpu.core.&lt;n&gt;</c>, zero-based) and fans (<c>fan.&lt;n&gt;</c>, zero-based). Keys are
/// matched case-insensitively so hand-edited configuration stays forgiving.
/// </remarks>
public static class SensorKeys
{
    /// <summary>Total CPU usage in percent.</summary>
    public const string CpuTotal = "cpu.total";

    /// <summary>Prefix of the per-core CPU usage keys: <c>cpu.core.0</c>, <c>cpu.core.1</c>, …</summary>
    public const string CpuCorePrefix = "cpu.core.";

    /// <summary>GPU usage in percent.</summary>
    public const string GpuUsage = "gpu.usage";

    /// <summary>GPU memory currently in use, in bytes.</summary>
    public const string GpuVramUsed = "gpu.vram.used";

    /// <summary>Total GPU memory, in bytes.</summary>
    public const string GpuVramTotal = "gpu.vram.total";

    /// <summary>GPU memory usage in percent (derived from used/total).</summary>
    public const string GpuVramPercent = "gpu.vram.percent";

    /// <summary>System RAM currently in use, in bytes.</summary>
    public const string RamUsed = "ram.used";

    /// <summary>Total system RAM, in bytes.</summary>
    public const string RamTotal = "ram.total";

    /// <summary>System RAM usage in percent.</summary>
    public const string RamPercent = "ram.percent";

    /// <summary>Overall disk activity in percent.</summary>
    public const string DiskActivity = "disk.activity";

    /// <summary>Disk read throughput in bytes per second.</summary>
    public const string DiskRead = "disk.read";

    /// <summary>Disk write throughput in bytes per second.</summary>
    public const string DiskWrite = "disk.write";

    /// <summary>Network upload throughput in bytes per second.</summary>
    public const string NetworkUp = "net.up";

    /// <summary>Network download throughput in bytes per second.</summary>
    public const string NetworkDown = "net.down";

    /// <summary>CPU temperature in degrees Celsius.</summary>
    public const string CpuTemperature = "temp.cpu";

    /// <summary>GPU temperature in degrees Celsius.</summary>
    public const string GpuTemperature = "temp.gpu";

    /// <summary>Prefix of the fan speed keys: <c>fan.0</c>, <c>fan.1</c>, …</summary>
    public const string FanPrefix = "fan.";

    /// <summary>Builds the key for one zero-based CPU core index, for example <c>cpu.core.3</c>.</summary>
    /// <param name="coreIndex">The zero-based core index.</param>
    /// <returns>The stable sensor key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="coreIndex"/> is negative.</exception>
    public static string CpuCore(int coreIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(coreIndex);
        return CpuCorePrefix + coreIndex.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Builds the key for one zero-based fan index, for example <c>fan.0</c>.</summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    /// <returns>The stable sensor key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="fanIndex"/> is negative.</exception>
    public static string Fan(int fanIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fanIndex);
        return FanPrefix + fanIndex.ToString(CultureInfo.InvariantCulture);
    }
}
