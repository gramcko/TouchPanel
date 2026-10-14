using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Page-selector presentation state (T-16, RF-091/M2-RF-911..913): the configured pages are offered
/// in order, the active page is mirrored, an unknown id is mirrored to the first-page fallback, a
/// redundant tap is a no-op, and a service failure is contained. The real switch (rebuild and
/// persistence) is covered by <see cref="TouchPanel.Tests.Config.PageSwitchCoordinatorTests"/>.
/// </summary>
public sealed class PageSwitcherViewModelTests
{
    [Fact]
    public void Pages_ListsTheConfiguredPagesInOrder()
    {
        var vm = Create(TwoPages(), out _);

        Assert.Equal(new[] { "first", "second" }, vm.Pages.Select(page => page.Id));
        Assert.Equal(new[] { "first", "second" }, vm.Pages.Select(page => page.DisplayName));
    }

    [Fact]
    public void ActivePage_IsTheValidatedConfiguredPage_AndIsMarkedActive()
    {
        var vm = Create(TwoPages(activePageId: "second"), out _);

        Assert.Equal("second", vm.ActivePageId);
        Assert.True(vm.Pages.Single(page => page.Id == "second").IsActive);
        Assert.False(vm.Pages.Single(page => page.Id == "first").IsActive);
    }

    [Fact]
    public void CanSwitch_IsTrueForMultiplePages_AndFalseForOne()
    {
        Assert.True(Create(TwoPages(), out _).CanSwitch);
        Assert.False(Create(OnePage(), out _).CanSwitch);
    }

    [Fact]
    public async Task SwitchTo_RequestsThePageAndMirrorsTheResolvedSelection()
    {
        var vm = Create(TwoPages(), out var service);

        var changed = await vm.SwitchToAsync("second");

        Assert.True(changed);
        Assert.Equal(["second"], service.Requests);
        Assert.Equal("second", vm.ActivePageId);
        Assert.True(vm.Pages.Single(page => page.Id == "second").IsActive);
        Assert.False(vm.Pages.Single(page => page.Id == "first").IsActive);
    }

    [Fact]
    public async Task SwitchTo_UnknownId_MirrorsTheFirstPageFallback()
    {
        var vm = Create(TwoPages(), out var service);
        service.ResolvedPageId = "first";

        var changed = await vm.SwitchToAsync("missing");

        Assert.True(changed);
        Assert.Equal(["missing"], service.Requests);
        Assert.Equal("first", vm.ActivePageId);
        Assert.True(vm.Pages.Single(page => page.Id == "first").IsActive);
    }

    [Fact]
    public async Task SwitchTo_NoChange_ReturnsFalseAndKeepsTheActivePage()
    {
        var vm = Create(TwoPages(activePageId: "second"), out var service);
        service.Changed = false;
        service.ResolvedPageId = "second";

        var changed = await vm.SwitchToAsync("second");

        Assert.False(changed);
        Assert.Equal("second", vm.ActivePageId);
    }

    [Fact]
    public async Task SwitchTo_ServiceThrows_ReturnsFalseAndKeepsTheActivePage()
    {
        var vm = Create(TwoPages(), out var service);
        service.Handler = _ => throw new InvalidOperationException("simulated failure");

        var changed = await vm.SwitchToAsync("second");

        Assert.False(changed);
        Assert.Equal("first", vm.ActivePageId);
    }

    [Fact]
    public async Task SelectPageCommand_InvokesTheSwitchForItsParameter()
    {
        var vm = Create(TwoPages(), out var service);

        vm.SelectPageCommand.Execute("second");

        // The fake completes synchronously, so the continuation has run by the time Execute returns.
        await Task.Yield();
        Assert.Equal(["second"], service.Requests);
        Assert.Equal("second", vm.ActivePageId);
    }

    [Fact]
    public void ApplyConfiguration_RefreshesPagesAndActivePageAfterReload()
    {
        var vm = Create(TwoPages(), out _);

        var reloaded = TwoPages(pages: ["alpha", "beta", "gamma"], activePageId: "beta");
        vm.ApplyConfiguration(reloaded);

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, vm.Pages.Select(page => page.Id));
        Assert.Equal("beta", vm.ActivePageId);
    }

    private static PageSwitcherViewModel Create(PanelConfig config, out FakePageSwitchService service)
    {
        service = new FakePageSwitchService();
        return new PageSwitcherViewModel(config, service);
    }

    private static PanelConfig OnePage() => TwoPages(pages: ["only"], activePageId: "only");

    private static PanelConfig TwoPages(
        string[]? pages = null,
        string activePageId = "first")
    {
        pages ??= ["first", "second"];
        return new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = activePageId,
            Pages = [.. pages.Select(id => new PageConfig { Id = id, Widgets = [] })],
        };
    }
}
