using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Pager presentation state (user-requested phone-homepage pagination): the active index/count,
/// the dots label, next/previous movement clamped at the ends, configured page names, and the
/// edit-mode refusal mirrored from <see cref="IPageSwitchService"/>.
/// </summary>
public sealed class PageSwitcherPagingTests
{
    [Fact]
    public void ActiveIndexCountAndIndicator_ReflectTheResolvedActivePage()
    {
        var vm = Create(TwoPages(active: "second"), out _);

        Assert.Equal(1, vm.ActiveIndex);
        Assert.Equal(2, vm.PageCount);
        Assert.Equal("2 / 2", vm.PageIndicatorText);
        Assert.True(vm.CanGoPrevious);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public async Task MoveBy_AdvancesAndReturnsWithinTheConfiguredOrder()
    {
        var vm = Create(ThreePages(active: "first"), out var service);

        Assert.True(await vm.MoveByAsync(1));
        Assert.Equal("second", vm.ActivePageId);
        Assert.Equal(1, vm.ActiveIndex);
        Assert.Equal("2 / 3", vm.PageIndicatorText);

        Assert.True(await vm.MoveByAsync(-1));
        Assert.Equal("first", vm.ActivePageId);
        Assert.Equal(["second", "first"], service.Requests);
    }

    [Fact]
    public async Task MoveBy_ClampsAtBothEndsWithoutRequestingASwitch()
    {
        var vm = Create(ThreePages(active: "first"), out var service);

        Assert.False(await vm.MoveByAsync(-1));
        Assert.Empty(service.Requests);

        vm = Create(ThreePages(active: "third"), out service);
        Assert.False(await vm.MoveByAsync(1));
        Assert.Empty(service.Requests);
        Assert.False(await vm.MoveByAsync(0));
    }

    [Fact]
    public async Task MoveBy_WhenTheSwitchIsRefused_KeepsTheActivePage()
    {
        var vm = Create(TwoPages(active: "first"), out var service);
        service.Changed = false;

        Assert.False(await vm.MoveByAsync(1));

        Assert.Equal("first", vm.ActivePageId);
        Assert.Equal(["second"], service.Requests);
        Assert.True(vm.Pages.Single(page => page.Id == "first").IsActive);
    }

    [Fact]
    public async Task NextAndPreviousCommands_UseTheSameMovementPath()
    {
        var vm = Create(TwoPages(active: "first"), out var service);

        vm.NextPageCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(["second"], service.Requests);
        Assert.Equal("second", vm.ActivePageId);

        vm.PreviousPageCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(["second", "first"], service.Requests);
        Assert.Equal("first", vm.ActivePageId);
    }

    [Fact]
    public void ApplyConfiguration_RefreshesNamesIndexAndCountAfterAReload()
    {
        var vm = Create(TwoPages(active: "first"), out _);
        var reloaded = ThreePages(active: "second");
        reloaded.Pages[1].Name = "Dashboard";

        vm.ApplyConfiguration(reloaded);

        Assert.Equal(3, vm.PageCount);
        Assert.Equal(1, vm.ActiveIndex);
        Assert.Equal("2 / 3", vm.PageIndicatorText);
        Assert.Equal("Dashboard", vm.Pages[1].DisplayName);
        Assert.Equal("first", vm.Pages[0].DisplayName);
    }

    private static PageSwitcherViewModel Create(PanelConfig config, out FakePageSwitchService service)
    {
        service = new FakePageSwitchService();
        return new PageSwitcherViewModel(config, service);
    }

    private static PanelConfig ThreePages(string active) =>
        Config(new[] { "first", "second", "third" }, active);

    private static PanelConfig TwoPages(string active) =>
        Config(new[] { "first", "second" }, active);

    private static PanelConfig Config(string[] ids, string active) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = active,
        Pages = [.. ids.Select(id => new PageConfig { Id = id, Widgets = [] })],
    };
}
