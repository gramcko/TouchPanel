using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Input;
using TouchPanel.Services.Notifications;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Hotkeys;
using TouchPanel.Views.Hotkeys;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Hotkey-editor window wiring (M3-RF-611/M3-RF-612/M3-RF-616/M3-RF-619): it is an ordinary
/// activating window (deliberate user action) whose affordances meet the touch minimum, it lists the
/// edited entries, and it shows the "unavailable this session" note without crashing. The XAML is
/// instantiated on an STA thread but never shown, so no desktop state is touched (MV-M3-02).
/// </summary>
public sealed class HotkeysEditorWindowTests
{
    [Fact]
    public async Task HotkeysEditorWindow_ExposesTheTouchEditorAndTheUnavailableNote()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewModel = CreateViewModel(hotkeysAvailable: false);
            var window = new HotkeysEditorWindow(viewModel);
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                var list = Assert.IsType<ListView>(window.FindName("HotkeyList"));
                var keySelector = Assert.IsType<ComboBox>(window.FindName("KeySelector"));
                var actionSelector = Assert.IsType<ComboBox>(window.FindName("ActionTypeSelector"));
                var newButton = Assert.IsType<Button>(window.FindName("NewButton"));
                var saveButton = Assert.IsType<Button>(window.FindName("SaveEntryButton"));
                var removeButton = Assert.IsType<Button>(window.FindName("RemoveButton"));
                var applyButton = Assert.IsType<Button>(window.FindName("ApplyButton"));
                var closeButton = Assert.IsType<Button>(window.FindName("CloseButton"));
                var unavailableNote = Assert.IsType<Border>(window.FindName("UnavailableNote"));

                return new
                {
                    ShowActivated = window.ShowActivated,
                    IsDataContext = ReferenceEquals(window.DataContext, viewModel),
                    ItemCount = list.Items.Count,
                    KeyOptionCount = keySelector.Items.Count,
                    ActionOptionCount = actionSelector.Items.Count,
                    NewMinHeight = newButton.MinHeight,
                    SaveMinHeight = saveButton.MinHeight,
                    RemoveMinHeight = removeButton.MinHeight,
                    ApplyMinHeight = applyButton.MinHeight,
                    CloseMinHeight = closeButton.MinHeight,
                    UnavailableVisible = unavailableNote.Visibility == Visibility.Visible,
                };
            }
            finally
            {
                window.Close();
            }
        });

        // M3-RF-616: a deliberate activating window.
        Assert.True(result.ShowActivated);
        Assert.True(result.IsDataContext);

        // The configured list and the shared catalogs are bound.
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(SendKeyCombination.FriendlyKeyNames.Count, result.KeyOptionCount);
        Assert.True(result.ActionOptionCount >= 5);

        // M3-RF-619 / RF-025: every affordance meets the 40 DIP touch minimum.
        Assert.True(result.NewMinHeight >= 40);
        Assert.True(result.SaveMinHeight >= 40);
        Assert.True(result.RemoveMinHeight >= 40);
        Assert.True(result.ApplyMinHeight >= 40);
        Assert.True(result.CloseMinHeight >= 40);

        // M3-RF-611: the service-unavailable note is shown without crashing.
        Assert.True(result.UnavailableVisible);
    }

    [Fact]
    public async Task HotkeysEditorWindow_WhenServiceIsAvailable_HidesTheNote()
    {
        using var sta = new StaThread();

        var visible = await sta.InvokeAsync(() =>
        {
            var viewModel = CreateViewModel(hotkeysAvailable: true);
            var window = new HotkeysEditorWindow(viewModel);
            try
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                var note = Assert.IsType<Border>(window.FindName("UnavailableNote"));
                return note.Visibility;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(Visibility.Collapsed, visible);
    }

    private static HotkeysEditorViewModel CreateViewModel(bool hotkeysAvailable)
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

        return new HotkeysEditorViewModel(
            model,
            new FakeSettingsService(),
            new RecordingNotifier(),
            hotkeysAvailable,
            logger: logger);
    }

    private sealed class RecordingNotifier : IUserNotifier
    {
        public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info)
        {
        }
    }
}
