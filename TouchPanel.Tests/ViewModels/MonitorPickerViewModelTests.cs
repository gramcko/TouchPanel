using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Services.Monitors;
using TouchPanel.Services.Notifications;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Monitor-picker presentation state (T-17, RF-093/M2-RF-931..934): discovery rows carry the stable
/// identity fields and current/primary markers, the identity block is derived deterministically, the
/// current target is highlighted, an unavailable target is surfaced, a selection is persisted
/// through the settings boundary (apply intent), and a monitor with no stable identity is warned
/// about. No WPF, no hardware (M2-RNF-124).
/// </summary>
public sealed class MonitorPickerViewModelTests
{
    [Fact]
    public void BuildIdentity_PrefersTheUnitHardwareId_DevicePath_AndFriendlyName()
    {
        var descriptor = FakeMonitorEnumerator.Descriptor(
            manufacturer: "DEL",
            productCode: "A0BC",
            serial: "4353",
            devicePath: @"\\?\DISPLAY#DELA0BC",
            friendlyName: "DELL U2415");

        var identity = MonitorPickerViewModel.BuildIdentity(descriptor);

        Assert.Equal("DEL-A0BC-4353", identity.HardwareId);
        Assert.Equal(@"\\?\DISPLAY#DELA0BC", identity.DevicePath);
        Assert.Equal("DELL U2415", identity.FriendlyName);
    }

    [Fact]
    public void BuildIdentity_FallsBackThroughTheStableFields()
    {
        var descriptor = new MonitorDescriptor
        {
            Manufacturer = "DEL",
            ProductCode = "A0BC",
            MonitorDeviceId = @"MONITOR\DELA0BC\0001",
        };

        var identity = MonitorPickerViewModel.BuildIdentity(descriptor);

        Assert.Equal("DEL-A0BC", identity.HardwareId);
        Assert.Equal(@"MONITOR\DELA0BC\0001", identity.DevicePath);
        Assert.Null(identity.FriendlyName);
        Assert.True(MonitorPickerViewModel.HasStableIdentity(descriptor));
    }

    [Fact]
    public void HasStableIdentity_IsFalseWhenNoStableFieldExists()
    {
        var descriptor = new MonitorDescriptor
        {
            Bounds = new PixelRect(0, 0, 1920, 1080),
            SourceGdiName = @"\\.\DISPLAY1",
        };

        Assert.False(MonitorPickerViewModel.HasStableIdentity(descriptor));
    }

    [Fact]
    public async Task Refresh_ListsMonitorsWithIdentitySummaryAndMarkers()
    {
        var config = ConfigWith(match: null);
        var vm = Create(config, out _, out var enumerator);
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(friendlyName: "Left", primary: true));
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(friendlyName: "Right", x: 1920, gdiName: @"\\.\DISPLAY2"));

        await vm.RefreshAsync();

        Assert.Equal(2, vm.Monitors.Count);
        Assert.Contains(vm.Monitors, item => item.IsPrimary);
        Assert.Contains("1920×1080", vm.Monitors[0].IdentitySummary, StringComparison.Ordinal);
        Assert.Contains("primary", vm.Monitors[0].IdentitySummary, StringComparison.Ordinal);
        Assert.False(vm.HasUnavailableTarget);
        Assert.Equal("No target monitor has been selected.", vm.TargetStatusText);
    }

    [Fact]
    public async Task Refresh_HighlightsTheCurrentTarget()
    {
        var config = ConfigWith(new MonitorIdentity { HardwareId = "DEL-A0BC-4353" });
        var vm = Create(config, out _, out var enumerator);
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(friendlyName: "Current"));
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(friendlyName: "Other", manufacturer: "ACR", productCode: "1234", serial: "9999"));

        await vm.RefreshAsync();

        Assert.True(vm.Monitors.Single(item => item.DisplayName == "Current").IsCurrent);
        Assert.False(vm.Monitors.Single(item => item.DisplayName == "Other").IsCurrent);
        Assert.False(vm.HasUnavailableTarget);
        Assert.Contains("Current target", vm.TargetStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_UnavailableTarget_IsSurfaced()
    {
        var config = ConfigWith(new MonitorIdentity { HardwareId = "GONE-0000" });
        var vm = Create(config, out _, out var enumerator);
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(friendlyName: "Present"));

        await vm.RefreshAsync();

        Assert.True(vm.HasUnavailableTarget);
        Assert.Contains("unavailable", vm.TargetStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.All(vm.Monitors, item => Assert.False(item.IsCurrent));
    }

    [Fact]
    public async Task Refresh_EnumerationFailure_DegradesToEmptyWithoutThrowing()
    {
        var vm = Create(ConfigWith(match: null), out _, out var enumerator);
        enumerator.ExceptionToThrow = new InvalidOperationException("simulated");

        await vm.RefreshAsync();

        Assert.Empty(vm.Monitors);
        Assert.Contains("could not be enumerated", vm.TargetStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Select_PersistsTheIdentityAndFallbackThroughTheSettingsBoundary()
    {
        var vm = Create(ConfigWith(match: null), out var settings, out var enumerator);
        enumerator.Monitors.Add(FakeMonitorEnumerator.Descriptor(friendlyName: "Target"));
        await vm.RefreshAsync();
        vm.Fallback = MonitorFallback.Primary;

        var applied = await vm.SelectAsync(vm.Monitors[0]);

        Assert.True(applied);
        var change = Assert.Single(settings.Changes);
        Assert.True(change.SetMonitorMatch);
        Assert.Equal("DEL-A0BC-4353", change.MonitorMatch!.HardwareId);
        Assert.Equal(MonitorFallback.Primary, change.MonitorFallback);
    }

    [Fact]
    public async Task Select_WithoutStableIdentity_WarnsBeforeSaving()
    {
        var config = ConfigWith(match: null);
        var settings = new FakeSettingsService();
        var enumerator = new FakeMonitorEnumerator();
        var notifier = new RecordingUserNotifier();
        var vm = new MonitorPickerViewModel(enumerator, settings, () => config.Monitor, notifier);

        enumerator.Monitors.Add(new MonitorDescriptor
        {
            Bounds = new PixelRect(0, 0, 1280, 720),
            SourceGdiName = @"\\.\DISPLAY1",
        });
        await vm.RefreshAsync();

        var applied = await vm.SelectAsync(vm.Monitors[0]);

        Assert.True(applied);
        Assert.Contains(notifier.Notifications, n => n.Severity == NotificationSeverity.Warning);
        Assert.Single(settings.Changes);
    }

    [Fact]
    public async Task ApplyFallback_RequestsOnlyTheFallbackPolicy()
    {
        var vm = Create(ConfigWith(match: new MonitorIdentity { HardwareId = "DEL-A0BC" }), out var settings, out _);

        var applied = await vm.ApplyFallbackAsync(MonitorFallback.Primary);

        Assert.True(applied);
        var change = Assert.Single(settings.Changes);
        Assert.False(change.SetMonitorMatch);
        Assert.Equal(MonitorFallback.Primary, change.MonitorFallback);
        Assert.Equal(MonitorFallback.Primary, vm.Fallback);
    }

    private static MonitorPickerViewModel Create(
        PanelConfig config,
        out FakeSettingsService settings,
        out FakeMonitorEnumerator enumerator)
    {
        settings = new FakeSettingsService();
        enumerator = new FakeMonitorEnumerator();
        var notifier = new RecordingUserNotifier();
        return new MonitorPickerViewModel(
            enumerator,
            settings,
            () => config.Monitor,
            notifier);
    }

    private static PanelConfig ConfigWith(MonitorIdentity? match)
    {
        var config = DefaultConfigGenerator.Create();
        config.Monitor.Match = match;
        return config;
    }
}
