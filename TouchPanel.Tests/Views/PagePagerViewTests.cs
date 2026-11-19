using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Paging;
using TouchPanel.Views;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Phone-homepage pager presentation (user-requested pages management): the dots/count indicator,
/// swipe direction/threshold wiring, one navigation per gesture, tap pass-through, and the edit-mode
/// gate. The view is instantiated on an STA thread but never shown, and the deterministic gesture
/// hooks exercise the same tracker/navigation path as real pointer preview events.
/// </summary>
public sealed class PagePagerViewTests
{
    [Fact]
    public async Task Pager_ShowsDotsCountAndTheCurrentPage()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var vm = CreateViewModel(active: "second");
            var view = new PageSwitcherView { DataContext = vm };
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var dots = Assert.IsType<ItemsControl>(view.FindName("PageDots"));
            var label = Assert.IsType<TextBlock>(view.FindName("PageIndicatorText"));
            var indicator = Assert.IsType<Border>(view.FindName("PageIndicator"));
            var tabs = Assert.IsType<ItemsControl>(view.FindName("PageItems"));

            return (
                dotCount: dots.Items.Count,
                label: label.Text,
                indicatorHitTest: indicator.IsHitTestVisible,
                tabCount: tabs.Items.Count,
                rootVisibility: Assert.IsType<Grid>(view.FindName("SelectorRoot")).Visibility);
        });

        Assert.Equal(3, result.dotCount);
        Assert.Equal("2 / 3", result.label);
        Assert.False(result.indicatorHitTest);
        Assert.Equal(3, result.tabCount);
        Assert.Equal(Visibility.Visible, result.rootVisibility);
    }

    [Fact]
    public async Task SwipeLeft_AdvancesExactlyOnePage_AndConsumesTheRelease()
    {
        using var sta = new StaThread();
        var service = new FakePageSwitchService();
        var vm = CreateViewModel(active: "first", service);

        var result = await sta.InvokeAsync(() =>
        {
            var view = new PageSwitcherView { DataContext = vm };
            var begin = view.BeginGestureForTest(400, 200);
            var belowThreshold = view.MoveGestureForTest(350, 200);
            var recognized = view.MoveGestureForTest(300, 200);
            var complete = view.CompleteGestureForTest(280, 200);
            var secondComplete = view.CompleteGestureForTest(280, 200);

            return (begin, belowThreshold, recognized, complete, secondComplete,
                recognizedAfter: view.IsSwipeRecognized);
        });

        Assert.True(result.begin);
        Assert.False(result.belowThreshold.Handled);
        Assert.True(result.recognized.Handled);
        Assert.True(result.complete.Handled);
        Assert.Equal(PageSwipeDirection.Next, result.complete.Navigation);
        Assert.False(result.secondComplete.Handled);
        Assert.Equal(PageSwipeDirection.None, result.secondComplete.Navigation);
        Assert.False(result.recognizedAfter);

        Assert.Equal(["second"], service.Requests);
        Assert.Equal("second", vm.ActivePageId);
        Assert.Equal("2 / 3", vm.PageIndicatorText);
    }

    [Fact]
    public async Task SwipeRight_MovesToThePreviousPage()
    {
        using var sta = new StaThread();
        var service = new FakePageSwitchService();
        var vm = CreateViewModel(active: "second", service);

        var complete = await sta.InvokeAsync(() =>
        {
            var view = new PageSwitcherView { DataContext = vm };
            view.BeginGestureForTest(100, 200);
            view.MoveGestureForTest(250, 200);
            return view.CompleteGestureForTest(270, 200);
        });

        Assert.True(complete.Handled);
        Assert.Equal(PageSwipeDirection.Previous, complete.Navigation);
        Assert.Equal(["first"], service.Requests);
        Assert.Equal("first", vm.ActivePageId);
    }

    [Fact]
    public async Task TapAndVerticalDrag_DoNotNavigateOrConsumeTheWidgetAction()
    {
        using var sta = new StaThread();
        var service = new FakePageSwitchService();
        var vm = CreateViewModel(active: "first", service);

        var result = await sta.InvokeAsync(() =>
        {
            var view = new PageSwitcherView { DataContext = vm };

            view.BeginGestureForTest(100, 100);
            var tap = view.CompleteGestureForTest(103, 102);

            view.BeginGestureForTest(100, 100);
            var verticalMove = view.MoveGestureForTest(110, 260);
            var verticalRelease = view.CompleteGestureForTest(110, 300);

            return (tap, verticalMove, verticalRelease);
        });

        Assert.False(result.tap.Handled);
        Assert.Equal(PageSwipeDirection.None, result.tap.Navigation);
        Assert.False(result.verticalMove.Handled);
        Assert.False(result.verticalRelease.Handled);
        Assert.Empty(service.Requests);
        Assert.Equal("first", vm.ActivePageId);
    }

    [Fact]
    public async Task SinglePagePanel_DoesNotInterceptWidgetGestures()
    {
        using var sta = new StaThread();
        var service = new FakePageSwitchService();
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "only",
            Pages = [new PageConfig { Id = "only", Widgets = [] }],
        };
        var vm = new PageSwitcherViewModel(config, service);

        var result = await sta.InvokeAsync(() =>
        {
            var view = new PageSwitcherView { DataContext = vm };
            var begin = view.BeginGestureForTest(400, 200);
            var move = view.MoveGestureForTest(200, 200);
            var complete = view.CompleteGestureForTest(180, 200);
            return (begin, move, complete);
        });

        Assert.False(result.begin);
        Assert.False(result.move.Handled);
        Assert.False(result.complete.Handled);
        Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task EditModeGate_DisablesThePagerGesture()
    {
        using var sta = new StaThread();
        var service = new FakePageSwitchService();
        var vm = CreateViewModel(active: "first", service);

        var result = await sta.InvokeAsync(() =>
        {
            var view = new PageSwitcherView
            {
                DataContext = vm,
                IsPagingEnabled = () => false,
            };

            var begin = view.BeginGestureForTest(400, 200);
            var move = view.MoveGestureForTest(200, 200);
            var complete = view.CompleteGestureForTest(180, 200);
            return (begin, move, complete);
        });

        Assert.False(result.begin);
        Assert.False(result.move.Handled);
        Assert.False(result.complete.Handled);
        Assert.Empty(service.Requests);
        Assert.Equal("first", vm.ActivePageId);
    }

    [Fact]
    public async Task EditModeGate_FromTheEditModeService_DoesNotSwallowEditPanning()
    {
        // Mirrors MainWindow: paging is disabled while edit mode is active, and the window-level
        // preview handlers bail out before marking anything handled, so a touch drag reaches the edit
        // panels' vertical panning (the property/pages ScrollViewers) instead of being eaten by the
        // swipe tracker. Normal-mode swiping still navigates.
        using var sta = new StaThread();
        var service = new FakePageSwitchService();
        var config = Config("first");
        var viewModel = new PageSwitcherViewModel(config, service);
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var editMode = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);

        var result = await sta.InvokeAsync(() =>
        {
            var view = new PageSwitcherView
            {
                DataContext = viewModel,
                IsPagingEnabled = () => !editMode.IsEditMode,
            };

            var normalBegin = view.BeginGestureForTest(400, 200);
            var normalMove = view.MoveGestureForTest(200, 200);
            var normalComplete = view.CompleteGestureForTest(180, 200);
            var normalRequests = service.Requests.Count;

            editMode.Enter(1024, 600);

            var editBegin = view.BeginGestureForTest(400, 200);
            var editMove = view.MoveGestureForTest(200, 200);
            var editComplete = view.CompleteGestureForTest(180, 200);

            return (
                IsEditMode: editMode.IsEditMode,
                NormalBegin: normalBegin,
                NormalMoveHandled: normalMove.Handled,
                NormalCompleteHandled: normalComplete.Handled,
                NormalRequests: normalRequests,
                EditBegin: editBegin,
                EditMoveHandled: editMove.Handled,
                EditCompleteHandled: editComplete.Handled,
                EditRequests: service.Requests.Count);
        });

        Assert.True(result.IsEditMode);
        Assert.True(result.NormalBegin, "Normal-mode swiping must stay tracked.");
        Assert.True(result.NormalMoveHandled, "Normal-mode swipes must still suppress the widget tap.");
        Assert.Equal(1, result.NormalRequests);

        Assert.False(result.EditBegin, "Edit mode must not start a page gesture.");
        Assert.False(result.EditMoveHandled, "Edit mode must leave the drag unhandled so touch panning works.");
        Assert.False(result.EditCompleteHandled, "Edit mode must leave the release unhandled so touch panning works.");
        Assert.Equal(1, result.EditRequests);
    }

    private static PageSwitcherViewModel CreateViewModel(
        string active,
        FakePageSwitchService? service = null) =>
        new(Config(active), service ?? new FakePageSwitchService());

    private static PanelConfig Config(string active) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = active,
        Pages =
        [
            new PageConfig { Id = "first", Widgets = [] },
            new PageConfig { Id = "second", Widgets = [] },
            new PageConfig { Id = "third", Widgets = [] },
        ],
    };
}
