using TouchPanel.Config;
using TouchPanel.Input;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Views;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Host wiring for the normal-mode long-press entry (UX fix #1 plus the empty-space addition): the
/// widget and empty-space coordinators both raise the host's single edit-mode request event, and a
/// short tap still raises nothing. The view is instantiated on an STA thread but never shown.
/// </summary>
public sealed class WidgetPageHostLongPressTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task EmptySpaceHold_RaisesTheEditModeRequest_AndShortTapDoesNot()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var host = new WidgetPageHost();
            var requests = 0;
            host.EditModeRequested += (_, _) => requests++;

            host.EmptySpaceLongPressForTest.Press(T0);
            var shortTapFired = host.EmptySpaceLongPressForTest.Elapsed(T0 + TimeSpan.FromMilliseconds(50));
            var shortTapRelease = host.EmptySpaceLongPressForTest.Release();
            var requestsAfterTap = requests;

            host.EmptySpaceLongPressForTest.Press(T0);
            var holdFired = host.EmptySpaceLongPressForTest.Elapsed(T0 + LongPressGate.DefaultThreshold);
            var holdRelease = host.EmptySpaceLongPressForTest.Release();

            return (shortTapFired, shortTapRelease, requestsAfterTap, holdFired, holdRelease, requests);
        });

        Assert.False(result.shortTapFired);
        Assert.False(result.shortTapRelease);
        Assert.Equal(0, result.requestsAfterTap);
        Assert.True(result.holdFired);
        Assert.True(result.holdRelease);
        Assert.Equal(1, result.requests);
    }

    [Fact]
    public async Task WidgetHold_StillRaisesTheEditModeRequest()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var config = new PanelConfig
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
                                Label = "Mute",
                            },
                        ],
                    },
                ],
            };

            var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());
            var host = new WidgetPageHost { DataContext = page };
            var requests = 0;
            host.EditModeRequested += (_, _) => requests++;

            host.WidgetLongPressForTest.Press(page.Widgets[0], T0);
            var fired = host.WidgetLongPressForTest.Elapsed(T0 + LongPressGate.DefaultThreshold);
            var suppressed = host.WidgetLongPressForTest.Release();

            return (fired, suppressed, requests);
        });

        Assert.True(result.fired);
        Assert.True(result.suppressed);
        Assert.Equal(1, result.requests);
    }
}
