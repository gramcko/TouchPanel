using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Input;
using TouchPanel.Interop;
using TouchPanel.Services.Notifications;
using TouchPanel.Services.Tray;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Hotkeys;
using TouchPanel.ViewModels.Settings;
using TouchPanel.Views.Hotkeys;
using TouchPanel.Views.MonitorPicker;
using TouchPanel.Views.Settings;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Canonical UI surface map (M4-RF-921/M4-RF-960/D-M4-7, AC-M4-08): each promoted concern is
/// hosted by exactly one surface. The settings window does not host hotkey or appearance editing,
/// and the hotkeys/appearance editor does not host startup/interval/monitor/import-export; the tray
/// menu hosts exactly the six documented commands. These assertions guard against duplicate or
/// competing editors without touching the delivered UI.
/// </summary>
/// <remarks>
/// <para><b>Automatic/runtime behaviors require no user surface (M4-RF-964).</b> The following
/// promoted behaviors are active, logged/observable, and covered by their own tests but
/// deliberately expose no editing control: <c>RF-070</c> throttled backend re-initialization,
/// <c>RF-082</c> hot reload, <c>RF-060</c> local shortcuts (a fixed app-defined set, so no
/// local-shortcut editor exists), and the runtime side of <c>RF-045</c> (toggle-state restoration;
/// its only user knob is the per-Toggle <c>persistState</c> flag reachable from the property
/// editor). "Supported" for these means active + observable + tested, not that a control exists.
/// </para>
/// <para>The windows are instantiated on an STA thread but never shown, so no desktop state is
/// touched; the real live behavior is verified manually (MV-M4-02).</para>
/// </remarks>
public sealed class SurfaceOwnershipTests
{
    // ---- Surface ownership: the view models expose no cross-surface concern ----

    [Fact]
    public void SettingsViewModel_ExposesNoHotkeyOrAppearanceEditing()
    {
        var forbidden = new[] { "hotkey", "appearance", "theme", "iconpack", "accent" };

        var members = PublicMemberNames(typeof(SettingsViewModel));

        Assert.DoesNotContain(
            members,
            name => forbidden.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void HotkeysEditorViewModel_ExposesNoSettingsWindowControls()
    {
        var forbidden = new[]
        {
            "alwaysontop", "startwithwindows", "pollinterval", "monitor", "import", "export",
            "configpath", "schemaversion",
        };

        var members = PublicMemberNames(typeof(HotkeysEditorViewModel));

        Assert.DoesNotContain(
            members,
            name => forbidden.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    // ---- Surface ownership: the built XAML hosts no cross-surface controls ----

    [Fact]
    public async Task SettingsWindow_HostsNoHotkeyOrAppearanceControls()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = CreateSettingsWindow();
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                return (
                    HasSettingsSurface: window.FindName("AlwaysOnTopCheckBox") is not null,
                    Forbidden: FoundNames(
                        window,
                        "HotkeyList",
                        "KeySelector",
                        "ActionTypeSelector",
                        "ActionPathText",
                        "NewButton",
                        "SaveEntryButton",
                        "ThemeSelector",
                        "IconPackSelector",
                        "AccentTextBox",
                        "SetAccentButton",
                        "AppearanceSection"));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(result.HasSettingsSurface, "The settings surface should be present and found by name.");
        Assert.Empty(result.Forbidden);
    }

    [Fact]
    public async Task HotkeysEditorWindow_HostsNoSettingsWindowControls()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var window = CreateHotkeysWindow();
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                return (
                    HasHotkeysSurface: window.FindName("HotkeyList") is not null,
                    Forbidden: FoundNames(
                        window,
                        "AlwaysOnTopCheckBox",
                        "StartWithWindowsCheckBox",
                        "PollIntervalSlider",
                        "ApplyIntervalButton",
                        "ImportButton",
                        "ExportButton",
                        "MonitorPicker",
                        "ConfigPathText",
                        "SchemaVersionText"));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(result.HasHotkeysSurface, "The hotkeys surface should be present and found by name.");
        Assert.Empty(result.Forbidden);
    }

    // ---- Tray menu: exactly the six documented commands ----

    [Fact]
    public void TrayMenu_HasExactlyTheCanonicalSixCommands()
    {
        var (logger, _) = TestLoggerFactory.Create();
        using var icon = new FakeTrayIcon();
        var service = new TrayService(icon, logger);

        try
        {
            // Mirrors the composition root's ordering (App.xaml.cs): the M1 items plus the Edit
            // Mode toggle, Settings…, and Hotkeys…, and nothing else.
            service.SetMenuItem(
                new TrayMenuItem(TrayMenuCommand.EditMode, "Edit Mode", IsChecked: false, IsCheckable: true),
                index: 1);
            service.SetMenuItem(new TrayMenuItem(TrayMenuCommand.Settings, "Settings…"), index: 2);
            service.SetMenuItem(new TrayMenuItem(TrayMenuCommand.Hotkeys, "Hotkeys…"), index: 3);

            var commands = service.GetMenuItems().Select(item => item.Command).ToArray();

            Assert.Equal(
                new[]
                {
                    TrayMenuCommand.ShowHidePanel,
                    TrayMenuCommand.EditMode,
                    TrayMenuCommand.Settings,
                    TrayMenuCommand.Hotkeys,
                    TrayMenuCommand.OpenConfigurationFolder,
                    TrayMenuCommand.Exit,
                },
                commands);
        }
        finally
        {
            service.Dispose();
        }
    }

    private static IReadOnlyList<string> PublicMemberNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(property => property.Name)
            .Concat(
                type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(method => method.DeclaringType == type && !method.IsSpecialName)
                    .Select(method => method.Name))
            .ToArray();

    private static IReadOnlyList<string> FoundNames(FrameworkElement element, params string[] names) =>
        names.Where(name => element.FindName(name) is not null).ToArray();

    private static SettingsWindow CreateSettingsWindow()
    {
        var config = DefaultConfigGenerator.Create();
        var picker = new MonitorPickerViewModel(
            new FakeMonitorEnumerator(),
            new FakeSettingsService(),
            () => config.Monitor);
        var settings = new SettingsViewModel(
            new FakeSettingsService(),
            () => config,
            importExport: null,
            new FakeConfigFilePicker(),
            "panel.json");
        return new SettingsWindow(settings, picker);
    }

    private static HotkeysEditorWindow CreateHotkeysWindow()
    {
        var (logger, _) = TestLoggerFactory.Create();
        var model = new HotkeyEditorModel(
        [
            new HotkeyConfig
            {
                Id = "hotkey-1",
                Key = "A",
                Modifiers = ["Ctrl"],
                Action = new MuteActionConfig(),
            },
        ]);
        var viewModel = new HotkeysEditorViewModel(
            model,
            new FakeSettingsService(),
            new RecordingNotifier(),
            hotkeysAvailable: true,
            logger: logger);
        return new HotkeysEditorWindow(viewModel);
    }

    /// <summary>Minimal non-native tray icon seam so the menu model can be exercised without a shell.</summary>
    private sealed class FakeTrayIcon : ITrayIcon
    {
        public bool IsRegistered => false;

        public int LastError => 0;

        public nint WindowHandle => 0;

        public event EventHandler<TrayIconMouseEventArgs>? MouseAction
        {
            add { }
            remove { }
        }

        public event EventHandler? TaskbarCreated
        {
            add { }
            remove { }
        }

        public bool Add(nint iconHandle, string? tooltip = null) => true;

        public bool ReAdd() => true;

        public bool Delete() => true;

        public bool ShowBalloon(string title, string message, NotificationSeverity severity) => true;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingNotifier : IUserNotifier
    {
        public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info)
        {
        }
    }
}
