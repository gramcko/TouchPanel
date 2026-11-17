using TouchPanel.Config;
using TouchPanel.Input;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Long-press-to-edit behavior (UX fix #1): holding a widget in normal mode enters edit mode and
/// suppresses the widget's tap; a short tap does not enter edit mode and still executes the bound
/// action exactly once. No hardware, window, or real timer is involved.
/// </summary>
public sealed class WidgetLongPressTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task LongPress_EntersEditModeAndSuppressesTheWidgetAction()
    {
        var executor = new RecordingActionExecutor();
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext { ActionExecutor = executor });
        var config = ConfigWithButton();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
        var widget = page.Widgets[0];

        var coordinator = new WidgetLongPressCoordinator(_ => service.Enter(1000, 500));

        Assert.False(service.IsEditMode);

        coordinator.Press(widget, T0);
        Assert.True(coordinator.Elapsed(T0 + LongPressGate.DefaultThreshold));

        // Entering edit mode is the only activation path; here it is reflected by the edit flag.
        Assert.True(service.IsEditMode);
        Assert.True(widget.IsEditMode);

        // The long press suppresses the tap, so the widget action must not execute.
        Assert.True(coordinator.Release());
        await widget.ActivateAsync();
        Assert.Equal(0, executor.InvocationCount);
    }

    [Fact]
    public async Task ShortTap_DoesNotEnterEditModeAndExecutesTheActionExactlyOnce()
    {
        var executor = new RecordingActionExecutor();
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext { ActionExecutor = executor });
        var config = ConfigWithButton();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
        var widget = page.Widgets[0];

        var entered = 0;
        var coordinator = new WidgetLongPressCoordinator(_ => entered++);

        coordinator.Press(widget, T0);
        Assert.False(coordinator.Elapsed(T0 + TimeSpan.FromMilliseconds(100)));
        var suppress = coordinator.Release();

        Assert.False(suppress);
        Assert.False(service.IsEditMode);
        Assert.Equal(0, entered);

        // The normal tap path runs the bound action exactly once.
        await widget.ActivateAsync();
        Assert.Equal(1, executor.InvocationCount);
    }

    [Fact]
    public void HorizontalSwipeMovement_IsRecognizedForLongPressCancellation()
    {
        var press = new System.Windows.Point(100, 100);

        Assert.True(TouchPanel.Views.WidgetPageHost.IsHorizontalSwipeAwayFromPress(press, new System.Windows.Point(80, 102)));
        Assert.False(TouchPanel.Views.WidgetPageHost.IsHorizontalSwipeAwayFromPress(press, new System.Windows.Point(104, 102)));
        Assert.False(TouchPanel.Views.WidgetPageHost.IsHorizontalSwipeAwayFromPress(press, new System.Windows.Point(102, 180)));
        Assert.False(TouchPanel.Views.WidgetPageHost.IsHorizontalSwipeAwayFromPress(press, new System.Windows.Point(140, 160)));
    }

    private static PanelConfig ConfigWithButton() => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = "main",
        Pages =
        [
            new PageConfig
            {
                Id = "main",
                Widgets =
                [
                    new ButtonWidgetConfig
                    {
                        Id = "b1",
                        X = 0,
                        Y = 0,
                        Width = 160,
                        Height = 120,
                        Action = new MuteActionConfig(),
                        Label = "Mute",
                    },
                ],
            },
        ],
    };
}
