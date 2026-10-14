using System.Globalization;
using System.Runtime.InteropServices;
using Serilog;
using TouchPanel.Services.Monitors;

namespace TouchPanel.Interop.Monitors;

/// <summary>
/// Production <see cref="IMonitorEnumerator"/> (DD-4, RNF-010): combines <c>QueryDisplayConfig</c>
/// (active display device paths, monitor friendly names, EDID manufacturer/product codes, source
/// GDI names), <c>EnumDisplayMonitors</c>/<c>GetMonitorInfo</c> (physical bounds, work area, primary
/// flag, GDI device name), and <c>EnumDisplayDevices</c> (monitor device instance identifiers).
/// No WMI or SetupAPI is used (DD-4). All native declarations live in
/// <see cref="Win32MonitorNative"/> in this file.
/// </summary>
/// <remarks>
/// <para><b>Robustness.</b> Partial native failures never throw: the affected source is logged and
/// the remaining usable monitors are returned (an empty list when nothing can be enumerated). A
/// descriptor is only reported when its bounds are known, because bounds are required for panel
/// placement; a display path without a matching GDI rectangle is logged and skipped.</para>
/// <para><b>Determinism.</b> The result is sorted by GDI device name and then device path, so the
/// matcher's stable-order tiebreak is reproducible across calls (RF-016).</para>
/// <para><b>No polling.</b> Enumeration only runs when <see cref="Enumerate"/> is called; T-08 calls
/// it at startup and on display-change events (RF-022).</para>
/// </remarks>
public sealed class Win32MonitorEnumerator : IMonitorEnumerator
{
    /// <summary>
    /// Stride used for the opaque <c>DISPLAYCONFIG_MODE_INFO</c> output buffer. The enumerator never
    /// reads mode information, so the buffer is deliberately over-sized (the documented structure is
    /// 64 bytes) instead of duplicating a layout Windows owns and can extend.
    /// </summary>
    private const int ModeInfoBufferStride = 128;

    private const int MaxQueryAttempts = 5;

    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Win32MonitorEnumerator"/> class.
    /// </summary>
    /// <param name="logger">Logger for partial-failure diagnostics; defaults to the global Serilog logger.</param>
    public Win32MonitorEnumerator(ILogger? logger = null)
    {
        _logger = (logger ?? Serilog.Log.Logger).ForContext<Win32MonitorEnumerator>();
    }

    /// <inheritdoc />
    public IReadOnlyList<MonitorDescriptor> Enumerate()
    {
        try
        {
            return EnumerateCore();
        }
        catch (Exception ex)
        {
            // The boundary contract is "never throw": an unexpected failure is logged and reported
            // as "no monitors", which the selection service turns into the RF-017 unavailable path.
            _logger.Error(ex, "Monitor enumeration: enumeration failed unexpectedly; no monitors are reported");
            return [];
        }
    }

    private IReadOnlyList<MonitorDescriptor> EnumerateCore()
    {
        var gdiMonitors = EnumerateGdiMonitors();
        var paths = QueryActiveDisplayPaths();
        var descriptors = new List<MonitorDescriptor>(gdiMonitors.Count);
        var correlated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            var gdiName = path.SourceName?.ViewGdiDeviceName;
            var gdi = string.IsNullOrWhiteSpace(gdiName)
                ? null
                : gdiMonitors.Find(monitor => string.Equals(monitor.DeviceName, gdiName, StringComparison.OrdinalIgnoreCase));

            if (gdi is null)
            {
                _logger.Warning(
                    "Monitor enumeration: an active display path has no matching GDI monitor and was skipped. SourceGdiName={SourceGdiName} DevicePath={DevicePath} FriendlyName={FriendlyName}",
                    gdiName,
                    NullIfEmpty(path.TargetName?.MonitorDevicePath),
                    NullIfEmpty(path.TargetName?.MonitorFriendlyDeviceName));
                continue;
            }

            correlated.Add(gdi.DeviceName);
            descriptors.Add(BuildDescriptor(gdi, path));
        }

        // When QueryDisplayConfig failed (or an active monitor has no path), the GDI monitors are
        // still exposed with bounds so placement can proceed; identity is enriched best-effort from
        // EnumDisplayDevices.
        foreach (var gdi in gdiMonitors)
        {
            if (!correlated.Contains(gdi.DeviceName))
            {
                descriptors.Add(BuildDescriptor(gdi, path: null));
            }
        }

        return descriptors
            .OrderBy(descriptor => descriptor.SourceGdiName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.DevicePath ?? string.Empty, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Enumerates the GDI monitors with their physical bounds, work area, primary flag, and device
    /// name. Failures are logged and produce an empty (or partial) list.
    /// </summary>
    private List<GdiMonitorInfo> EnumerateGdiMonitors()
    {
        var monitors = new List<GdiMonitorInfo>();

        try
        {
            Win32MonitorNative.MonitorEnumProc callback = (hMonitor, _, _, _) =>
            {
                var info = new Win32MonitorNative.MonitorInfoEx
                {
                    cbSize = (uint)Marshal.SizeOf<Win32MonitorNative.MonitorInfoEx>(),
                    szDevice = string.Empty,
                };

                if (Win32MonitorNative.GetMonitorInfo(hMonitor, ref info))
                {
                    monitors.Add(new GdiMonitorInfo(
                        info.szDevice ?? string.Empty,
                        ToPixelRect(info.rcMonitor),
                        ToPixelRect(info.rcWork),
                        (info.dwFlags & Win32MonitorNative.MonitorInfoPrimary) != 0));
                }
                else
                {
                    _logger.Debug(
                        "Monitor enumeration: GetMonitorInfo failed for a monitor; it was skipped. Win32Error={Win32Error}",
                        Marshal.GetLastWin32Error());
                }

                return true;
            };

            if (!Win32MonitorNative.EnumDisplayMonitors(0, 0, callback, 0))
            {
                _logger.Warning(
                    "Monitor enumeration: EnumDisplayMonitors failed. Win32Error={Win32Error}",
                    Marshal.GetLastWin32Error());
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Monitor enumeration: enumerating GDI monitors failed");
        }

        return monitors;
    }

    /// <summary>
    /// Reads the active display paths via <c>QueryDisplayConfig</c>, retrying when the topology
    /// changes between the buffer-size query and the read. Failures are logged and produce an empty
    /// list (the caller falls back to bounds-only descriptors).
    /// </summary>
    private List<ActiveDisplayPath> QueryActiveDisplayPaths()
    {
        var result = new List<ActiveDisplayPath>();

        for (var attempt = 0; attempt < MaxQueryAttempts; attempt++)
        {
            var status = Win32MonitorNative.GetDisplayConfigBufferSizes(
                Win32MonitorNative.QdcOnlyActivePaths,
                out var pathCount,
                out var modeCount);

            if (status != Win32MonitorNative.ErrorSuccess)
            {
                _logger.Warning(
                    "Monitor enumeration: GetDisplayConfigBufferSizes failed; falling back to bounds-only descriptors. Win32Error={Win32Error}",
                    status);
                return result;
            }

            if (pathCount == 0)
            {
                return result;
            }

            var pathStride = Marshal.SizeOf<Win32MonitorNative.DisplayConfigPathInfo>();
            var pathBuffer = Marshal.AllocHGlobal(checked((int)pathCount * pathStride));
            var modeBuffer = nint.Zero;

            try
            {
                if (modeCount > 0)
                {
                    // The mode array is an opaque output buffer the enumerator never reads; see
                    // ModeInfoBufferStride. Over-sizing it is safe because Windows only writes
                    // modeCount elements into the buffer it was told about.
                    modeBuffer = Marshal.AllocHGlobal(checked((int)modeCount * ModeInfoBufferStride));
                }

                status = Win32MonitorNative.QueryDisplayConfig(
                    Win32MonitorNative.QdcOnlyActivePaths,
                    ref pathCount,
                    pathBuffer,
                    ref modeCount,
                    modeBuffer,
                    nint.Zero);

                if (status == Win32MonitorNative.ErrorInsufficientBuffer)
                {
                    continue; // Topology changed; query fresh sizes and retry.
                }

                if (status != Win32MonitorNative.ErrorSuccess)
                {
                    _logger.Warning(
                        "Monitor enumeration: QueryDisplayConfig failed; falling back to bounds-only descriptors. Win32Error={Win32Error}",
                        status);
                    return result;
                }

                for (var index = 0; index < pathCount; index++)
                {
                    var path = Marshal.PtrToStructure<Win32MonitorNative.DisplayConfigPathInfo>(
                        nint.Add(pathBuffer, index * pathStride));
                    result.Add(new ActiveDisplayPath(TryGetTargetName(path), TryGetSourceName(path)));
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(pathBuffer);

                if (modeBuffer != nint.Zero)
                {
                    Marshal.FreeHGlobal(modeBuffer);
                }
            }
        }

        _logger.Warning("Monitor enumeration: the active display topology kept changing; falling back to bounds-only descriptors");
        return result;
    }

    private static Win32MonitorNative.DisplayConfigTargetDeviceName? TryGetTargetName(in Win32MonitorNative.DisplayConfigPathInfo path)
    {
        var target = new Win32MonitorNative.DisplayConfigTargetDeviceName
        {
            Header = new Win32MonitorNative.DisplayConfigDeviceInfoHeader
            {
                Type = Win32MonitorNative.DeviceInfoGetTargetName,
                Size = (uint)Marshal.SizeOf<Win32MonitorNative.DisplayConfigTargetDeviceName>(),
                AdapterId = path.Target.AdapterId,
                Id = path.Target.Id,
            },
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty,
        };

        return Win32MonitorNative.DisplayConfigGetDeviceInfo(ref target) == Win32MonitorNative.ErrorSuccess
            ? target
            : null;
    }

    private static Win32MonitorNative.DisplayConfigSourceDeviceName? TryGetSourceName(in Win32MonitorNative.DisplayConfigPathInfo path)
    {
        var source = new Win32MonitorNative.DisplayConfigSourceDeviceName
        {
            Header = new Win32MonitorNative.DisplayConfigDeviceInfoHeader
            {
                Type = Win32MonitorNative.DeviceInfoGetSourceName,
                Size = (uint)Marshal.SizeOf<Win32MonitorNative.DisplayConfigSourceDeviceName>(),
                AdapterId = path.Source.AdapterId,
                Id = path.Source.Id,
            },
            ViewGdiDeviceName = string.Empty,
        };

        return Win32MonitorNative.DisplayConfigGetDeviceInfo(ref source) == Win32MonitorNative.ErrorSuccess
            ? source
            : null;
    }

    /// <summary>
    /// Builds the descriptor for one GDI monitor, merging the display-path identity when available
    /// and filling gaps best-effort from the device path and <c>EnumDisplayDevices</c>.
    /// </summary>
    private static MonitorDescriptor BuildDescriptor(GdiMonitorInfo gdi, ActiveDisplayPath? path)
    {
        var friendlyName = NullIfEmpty(path?.TargetName?.MonitorFriendlyDeviceName);
        var devicePath = NullIfEmpty(path?.TargetName?.MonitorDevicePath);

        var deviceInfo = GetMonitorDeviceInfo(gdi.DeviceName);
        devicePath ??= deviceInfo.InterfacePath;
        friendlyName ??= deviceInfo.DeviceString;

        string? manufacturer = null;
        string? productCode = null;

        if (path?.TargetName is { } target)
        {
            manufacturer = Win32MonitorNative.DecodeEdidManufacturer(target.EdidManufactureId);
            productCode = Win32MonitorNative.DecodeEdidProductCode(target.EdidProductCodeId);
        }

        // Best-effort path parsing fills gaps the driver did not report (for example a missing EDID
        // block) and extracts the unit token used as the serial when present.
        Win32MonitorNative.TryParseDevicePathIdentity(
            devicePath ?? deviceInfo.InstanceId,
            out var pathManufacturer,
            out var pathProductCode,
            out var serialNumber);

        manufacturer ??= pathManufacturer;
        productCode ??= pathProductCode;

        return new MonitorDescriptor
        {
            DevicePath = devicePath,
            MonitorDeviceId = deviceInfo.InstanceId ?? deviceInfo.InterfacePath,
            Manufacturer = manufacturer,
            ProductCode = productCode,
            SerialNumber = serialNumber,
            FriendlyName = friendlyName,
            Bounds = gdi.Bounds,
            WorkArea = gdi.WorkArea,
            IsPrimary = gdi.IsPrimary,
            SourceGdiName = NullIfEmpty(gdi.DeviceName),
        };
    }

    private static MonitorDeviceInfo GetMonitorDeviceInfo(string? gdiAdapterName)
    {
        if (string.IsNullOrWhiteSpace(gdiAdapterName))
        {
            return default;
        }

        string? instanceId = null;
        string? interfacePath = null;
        string? deviceString = null;

        var device = CreateDisplayDevice();
        if (Win32MonitorNative.EnumDisplayDevices(gdiAdapterName, 0, ref device, 0))
        {
            instanceId = NullIfEmpty(device.DeviceID);
            deviceString = NullIfEmpty(device.DeviceString);
        }

        device = CreateDisplayDevice();
        if (Win32MonitorNative.EnumDisplayDevices(gdiAdapterName, 0, ref device, Win32MonitorNative.EddGetDeviceInterfaceName))
        {
            interfacePath = NullIfEmpty(device.DeviceID);
            deviceString ??= NullIfEmpty(device.DeviceString);
        }

        return new MonitorDeviceInfo(instanceId, interfacePath, deviceString);
    }

    private static Win32MonitorNative.DisplayDevice CreateDisplayDevice() => new()
    {
        cb = (uint)Marshal.SizeOf<Win32MonitorNative.DisplayDevice>(),
        DeviceName = string.Empty,
        DeviceString = string.Empty,
        DeviceID = string.Empty,
        DeviceKey = string.Empty,
    };

    private static PixelRect ToPixelRect(in Win32MonitorNative.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record GdiMonitorInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary);

    private sealed record ActiveDisplayPath(
        Win32MonitorNative.DisplayConfigTargetDeviceName? TargetName,
        Win32MonitorNative.DisplayConfigSourceDeviceName? SourceName);

    private readonly record struct MonitorDeviceInfo(string? InstanceId, string? InterfacePath, string? DeviceString);
}

/// <summary>
/// Raw user32 entry points and native structures for monitor enumeration (RNF-010: all P/Invoke
/// stays under <c>Interop/</c>). Wrapped by <see cref="Win32MonitorEnumerator"/>; the decoding and
/// device-path parsing helpers are pure and unit-tested without hardware.
/// </summary>
internal static class Win32MonitorNative
{
    /// <summary><c>QDC_ONLY_ACTIVE_PATHS</c>: only paths that are currently active.</summary>
    internal const uint QdcOnlyActivePaths = 0x00000002;

    /// <summary><c>DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME</c>.</summary>
    internal const uint DeviceInfoGetSourceName = 1;

    /// <summary><c>DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME</c>.</summary>
    internal const uint DeviceInfoGetTargetName = 2;

    /// <summary><c>ERROR_SUCCESS</c>.</summary>
    internal const int ErrorSuccess = 0;

    /// <summary><c>ERROR_INSUFFICIENT_BUFFER</c>: the topology changed between the size query and the read.</summary>
    internal const int ErrorInsufficientBuffer = 122;

    /// <summary><c>EDD_GET_DEVICE_INTERFACE_NAME</c>: return the device interface path in <c>DeviceID</c>.</summary>
    internal const uint EddGetDeviceInterfaceName = 0x00000001;

    /// <summary><c>MONITORINFOF_PRIMARY</c>.</summary>
    internal const uint MonitorInfoPrimary = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        public uint cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public uint cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        /// <summary>
        /// <c>DISPLAY_DEVICE_STATE_FLAGS</c>. Must stay between <c>DeviceString</c> and
        /// <c>DeviceID</c>: the native layout places <c>DeviceID</c> at offset 328 on x64.
        /// </summary>
        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo Source;
        public DisplayConfigPathTargetInfo Target;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate bool MonitorEnumProc(nint hMonitor, nint hdc, nint lprcMonitor, nint dwData);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    /// <summary>
    /// <c>QueryDisplayConfig</c>. Both output arrays are passed as raw unmanaged buffers: the path
    /// array is read back with <c>Marshal.PtrToStructure</c> and the mode array is never read.
    /// <c>currentTopologyId</c> is always NULL: it is only meaningful with
    /// <c>QDC_DATABASE_CURRENT</c>, and current Windows builds reject a non-NULL pointer with
    /// <c>ERROR_INVALID_PARAMETER</c>.
    /// </summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        nint pathInfoArray,
        ref uint numModeInfoArrayElements,
        nint modeInfoArray,
        nint currentTopologyId);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName deviceName);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName deviceName);

    /// <summary>
    /// Decodes the 16-bit EDID manufacturer identifier into its three-letter PnP code (for example
    /// <c>0x10AC</c> to <c>DEL</c>). Returns <see langword="null"/> when neither the value nor its
    /// byte-swapped form decodes to valid letters (some drivers report the swapped value).
    /// </summary>
    /// <param name="edidManufactureId">The <c>edidManufactureId</c> value from <c>DISPLAYCONFIG_TARGET_DEVICE_NAME</c>.</param>
    /// <returns>The three-letter manufacturer code, or <see langword="null"/> when unavailable.</returns>
    internal static string? DecodeEdidManufacturer(ushort edidManufactureId)
    {
        if (TryDecodeManufacturerValue(edidManufactureId, out var manufacturer))
        {
            return manufacturer;
        }

        var swapped = (ushort)((edidManufactureId >> 8) | (edidManufactureId << 8));
        return TryDecodeManufacturerValue(swapped, out manufacturer) ? manufacturer : null;
    }

    /// <summary>
    /// Formats the 16-bit EDID product code as four uppercase hexadecimal digits (for example
    /// <c>0xA0BC</c> to <c>A0BC</c>); returns <see langword="null"/> for the "not reported" zero.
    /// </summary>
    /// <param name="edidProductCodeId">The <c>edidProductCodeId</c> value from <c>DISPLAYCONFIG_TARGET_DEVICE_NAME</c>.</param>
    /// <returns>The product code, or <see langword="null"/> when unavailable.</returns>
    internal static string? DecodeEdidProductCode(ushort edidProductCodeId) =>
        edidProductCodeId == 0 ? null : edidProductCodeId.ToString("X4", CultureInfo.InvariantCulture);

    /// <summary>
    /// Best-effort parsing of a display device path into its EDID model identity and unit token
    /// (DD-4: serial matching is best-effort from the device path when present).
    /// <list type="bullet">
    /// <item><description>Interface path: <c>\\?\DISPLAY#DELA0BC#5&amp;2f1a3b4c&amp;0&amp;UID4353#{guid}</c> yields
    /// <c>DEL</c>, <c>A0BC</c>, and serial <c>4353</c>.</description></item>
    /// <item><description>Device instance path: <c>MONITOR\DELA0BC\{class-guid}\0001</c> yields
    /// <c>DEL</c>, <c>A0BC</c>, and no serial (the trailing index is not a serial).</description></item>
    /// </list>
    /// </summary>
    /// <param name="devicePath">The display device path to parse; may be <see langword="null"/>.</param>
    /// <param name="manufacturer">Receives the three-letter manufacturer code when parsed.</param>
    /// <param name="productCode">Receives the four-digit product code when parsed.</param>
    /// <param name="serialNumber">Receives the best-effort unit token when present.</param>
    /// <returns><see langword="true"/> when at least one identity part was parsed.</returns>
    internal static bool TryParseDevicePathIdentity(
        string? devicePath,
        out string? manufacturer,
        out string? productCode,
        out string? serialNumber)
    {
        manufacturer = null;
        productCode = null;
        serialNumber = null;

        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return false;
        }

        var parsed = false;

        if (devicePath.Contains('#'))
        {
            var segments = devicePath.Split('#');
            if (segments.Length >= 2)
            {
                parsed |= TryParseEdidModelSegment(segments[1], out manufacturer, out productCode);
            }

            if (segments.Length >= 3)
            {
                serialNumber = ExtractSerialToken(segments[2]);
                parsed |= serialNumber is not null;
            }
        }
        else
        {
            foreach (var segment in devicePath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseEdidModelSegment(segment, out manufacturer, out productCode))
                {
                    parsed = true;
                    break;
                }
            }
        }

        return parsed;
    }

    private static bool TryDecodeManufacturerValue(ushort value, out string manufacturer)
    {
        Span<char> letters = stackalloc char[3];
        for (var index = 0; index < letters.Length; index++)
        {
            var letter = (value >> (10 - (index * 5))) & 0x1F;
            if (letter is < 1 or > 26)
            {
                manufacturer = string.Empty;
                return false;
            }

            letters[index] = (char)('A' + letter - 1);
        }

        manufacturer = new string(letters);
        return true;
    }

    private static bool TryParseEdidModelSegment(string segment, out string? manufacturer, out string? productCode)
    {
        manufacturer = null;
        productCode = null;

        if (segment.Length != 7)
        {
            return false;
        }

        for (var index = 0; index < 3; index++)
        {
            if (!char.IsAsciiLetter(segment[index]))
            {
                return false;
            }
        }

        for (var index = 3; index < 7; index++)
        {
            if (!Uri.IsHexDigit(segment[index]))
            {
                return false;
            }
        }

        manufacturer = segment[..3].ToUpperInvariant();
        productCode = segment[3..].ToUpperInvariant();
        return true;
    }

    private static string? ExtractSerialToken(string instanceSegment)
    {
        var index = instanceSegment.IndexOf("UID", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var token = instanceSegment[(index + 3)..].TrimStart('_', '&');
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
