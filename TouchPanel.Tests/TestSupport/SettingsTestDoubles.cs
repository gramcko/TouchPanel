using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Services.Monitors;
using TouchPanel.Services.Startup;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IStartupRegistration"/> double (M2-RNF-124): records every applied
/// start-with-Windows value and returns a configurable result (or throws) without touching the
/// real user hive.
/// </summary>
internal sealed class FakeStartupRegistration : IStartupRegistration
{
    /// <summary>Gets the start-with-Windows values applied, in call order.</summary>
    public List<bool> Applied { get; } = [];

    /// <summary>Gets or sets the result returned by <see cref="Apply"/>.</summary>
    public StartupRegistrationResult Result { get; set; } = StartupRegistrationResult.NoChange();

    /// <summary>Gets or sets an exception thrown by <see cref="Apply"/> instead of returning a result.</summary>
    public Exception? ThrowOnApply { get; set; }

    /// <inheritdoc />
    public StartupRegistrationSnapshot Read() => new()
    {
        Succeeded = true,
        Exists = false,
        IsCurrent = false,
    };

    /// <inheritdoc />
    public StartupRegistrationResult Apply(bool startWithWindows)
    {
        Applied.Add(startWithWindows);
        if (ThrowOnApply is { } exception)
        {
            throw exception;
        }

        return Result;
    }
}

/// <summary>
/// Recording <see cref="ISettingsService"/> double (M2-RNF-124): captures the requested
/// <see cref="SettingsChange"/> values and returns a configurable result so the settings and
/// monitor-picker view models are testable without a store or live apply target.
/// </summary>
internal sealed class FakeSettingsService : ISettingsService
{
    /// <summary>Gets the changes requested, in call order.</summary>
    public List<SettingsChange> Changes { get; } = [];

    /// <summary>Gets or sets the result returned when no <see cref="Handler"/> is set.</summary>
    public SettingsApplyResult Result { get; set; } = SettingsApplyResult.Success(new PanelConfig());

    /// <summary>Gets or sets a per-call result/handler override (may throw to exercise containment).</summary>
    public Func<SettingsChange, SettingsApplyResult>? Handler { get; set; }

    /// <inheritdoc />
    public Task<SettingsApplyResult> ApplyAsync(SettingsChange change, CancellationToken cancellationToken = default)
    {
        Changes.Add(change);
        return Task.FromResult(Handler?.Invoke(change) ?? Result);
    }
}

/// <summary>
/// Deterministic <see cref="IConfigFilePicker"/> double: returns configured import/export paths and
/// records pick attempts.
/// </summary>
internal sealed class FakeConfigFilePicker : IConfigFilePicker
{
    /// <summary>Gets or sets the path returned by <see cref="PickImportPath"/>.</summary>
    public string? ImportPath { get; set; }

    /// <summary>Gets or sets the path returned by <see cref="PickExportPath"/>.</summary>
    public string? ExportPath { get; set; }

    /// <summary>Gets the number of import pick attempts.</summary>
    public int ImportPickCount { get; private set; }

    /// <summary>Gets the number of export pick attempts.</summary>
    public int ExportPickCount { get; private set; }

    /// <inheritdoc />
    public string? PickImportPath()
    {
        ImportPickCount++;
        return ImportPath;
    }

    /// <inheritdoc />
    public string? PickExportPath()
    {
        ExportPickCount++;
        return ExportPath;
    }
}

/// <summary>
/// Deterministic <see cref="IMonitorEnumerator"/> double: returns the configured descriptors (or
/// throws) and records the call count.
/// </summary>
internal sealed class FakeMonitorEnumerator : IMonitorEnumerator
{
    /// <summary>Gets the descriptors returned by <see cref="Enumerate"/>.</summary>
    public List<MonitorDescriptor> Monitors { get; } = [];

    /// <summary>Gets or sets an exception thrown instead of returning descriptors.</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>Gets the number of enumeration calls.</summary>
    public int CallCount { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<MonitorDescriptor> Enumerate()
    {
        CallCount++;
        if (ExceptionToThrow is { } exception)
        {
            throw exception;
        }

        return Monitors.ToArray();
    }

    /// <summary>Creates a descriptor with the supplied identity/output fields.</summary>
    /// <param name="manufacturer">EDID manufacturer (for example <c>DEL</c>).</param>
    /// <param name="productCode">EDID product code (for example <c>A0BC</c>).</param>
    /// <param name="serial">Best-effort serial token.</param>
    /// <param name="friendlyName">Windows friendly name.</param>
    /// <param name="devicePath">Display device path.</param>
    /// <param name="monitorDeviceId">Device instance identifier.</param>
    /// <param name="primary">Whether this is the primary monitor.</param>
    /// <param name="x">Left edge in physical pixels.</param>
    /// <param name="y">Top edge in physical pixels.</param>
    /// <param name="gdiName">Source GDI name.</param>
    /// <returns>The descriptor.</returns>
    public static MonitorDescriptor Descriptor(
        string? manufacturer = "DEL",
        string? productCode = "A0BC",
        string? serial = "4353",
        string? friendlyName = "DELL U2415",
        string? devicePath = @"\\?\DISPLAY#DELA0BC#5&2f1a3b4c&0&UID4353",
        string? monitorDeviceId = null,
        bool primary = false,
        int x = 0,
        int y = 0,
        string? gdiName = @"\\.\DISPLAY1") => new()
        {
            Manufacturer = manufacturer,
            ProductCode = productCode,
            SerialNumber = serial,
            FriendlyName = friendlyName,
            DevicePath = devicePath,
            MonitorDeviceId = monitorDeviceId,
            IsPrimary = primary,
            Bounds = new PixelRect(x, y, 1920, 1080),
            WorkArea = new PixelRect(x, y, 1920, 1040),
            SourceGdiName = gdiName,
        };
}
