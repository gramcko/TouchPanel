using TouchPanel.Config;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Pure page-list management (user-requested pages management): unique ids, sensible generated
/// names, rename with order/widget preservation, the last-page guard, and the deterministic
/// first-page fallback when the active page is removed.
/// </summary>
public sealed class PageListOperationsTests
{
    [Fact]
    public void AddPage_AppendsAUniqueIdAndGeneratedName()
    {
        var pages = Pages("main", "page-1");

        var result = PageListOperations.AddPage(pages);

        Assert.Equal(3, result.Pages.Count);
        Assert.Same(result.AddedPage, result.Pages[^1]);
        Assert.Equal("page-2", result.AddedPage.Id);
        Assert.Equal("Page 1", result.AddedPage.Name);
        Assert.Empty(result.AddedPage.Widgets);
        Assert.Equal(new[] { "main", "page-1", "page-2" }, result.Pages.Select(page => page.Id));
    }

    [Fact]
    public void AddPage_UsesATrimmedRequestedName_AndANonCollidingGeneratedName()
    {
        var first = PageListOperations.AddPage(Pages("main"), "  Media  ");
        Assert.Equal("Media", first.AddedPage.Name);

        var second = PageListOperations.AddPage(first.Pages, " ");
        Assert.Equal("Page 1", second.AddedPage.Name);

        var third = PageListOperations.AddPage(second.Pages, "Page 1");
        Assert.Equal("Page 2", PageListOperations.CreateUniquePageName(third.Pages));
    }

    [Fact]
    public void AddPage_TruncatesAnOverLongName()
    {
        var name = new string('a', PageListOperations.MaxPageNameLength + 20);

        var result = PageListOperations.AddPage(Pages("main"), name);

        Assert.Equal(PageListOperations.MaxPageNameLength, result.AddedPage.Name!.Length);
    }

    [Fact]
    public void DisplayName_PrefersTheConfiguredName_AndFallsBackToTheId()
    {
        Assert.Equal("main", PageListOperations.DisplayName(new PageConfig { Id = "main" }));
        Assert.Equal("Dashboard", PageListOperations.DisplayName(new PageConfig { Id = "main", Name = "Dashboard" }));
    }

    [Fact]
    public void Rename_PreservesOrderIdAndWidgets()
    {
        var widgets = new List<WidgetConfig> { new LabelWidgetConfig { Id = "label" } };
        var pages = new[]
        {
            new PageConfig { Id = "main", Widgets = widgets },
            new PageConfig { Id = "second", Widgets = [] },
        };

        var renamed = PageListOperations.TryRenamePage(pages, "main", "  Home  ", out var updated, out var name);

        Assert.True(renamed);
        Assert.Equal("Home", name);
        Assert.Equal(new[] { "main", "second" }, updated.Select(page => page.Id));
        Assert.Equal("Home", updated[0].Name);
        Assert.Same(widgets, updated[0].Widgets);
        Assert.NotSame(pages[0], updated[0]);
        Assert.Same(pages[1], updated[1]);
    }

    [Fact]
    public void Rename_RefusesBlankUnknownOrMissingPages()
    {
        var pages = Pages("main");

        Assert.False(PageListOperations.TryRenamePage(pages, "main", "   ", out var unchanged, out _));
        Assert.Same(pages, unchanged);
        Assert.False(PageListOperations.TryRenamePage(pages, "missing", "Name", out _, out _));
        Assert.False(PageListOperations.TryRenamePage(pages, "main", null, out _, out _));
    }

    [Fact]
    public void Remove_InactivePage_KeepsTheActivePage()
    {
        var pages = Pages("main", "second", "third");

        var result = PageListOperations.RemovePage(pages, "second", "main");

        Assert.True(result.Removed);
        Assert.False(result.ActivePageFellBack);
        Assert.Equal("main", result.ActivePageId);
        Assert.Equal(new[] { "main", "third" }, result.Pages.Select(page => page.Id));
    }

    [Fact]
    public void Remove_ActivePage_FallsBackToTheFirstRemainingPage()
    {
        var pages = Pages("main", "second", "third");

        var result = PageListOperations.RemovePage(pages, "second", "second");

        Assert.True(result.Removed);
        Assert.True(result.ActivePageFellBack);
        Assert.Equal("main", result.ActivePageId);
        Assert.Equal(new[] { "main", "third" }, result.Pages.Select(page => page.Id));
    }

    [Fact]
    public void Remove_LastPage_IsRefused()
    {
        var pages = Pages("only");

        var result = PageListOperations.RemovePage(pages, "only", "only");

        Assert.False(result.Removed);
        Assert.Same(pages, result.Pages);
        Assert.Equal("only", result.ActivePageId);
        Assert.False(result.ActivePageFellBack);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Remove_UnknownPage_IsRefusedWithoutChangingTheList()
    {
        var pages = Pages("main", "second");

        var result = PageListOperations.RemovePage(pages, "missing", "second");

        Assert.False(result.Removed);
        Assert.Same(pages, result.Pages);
        Assert.Equal("second", result.ActivePageId);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void CreateUniquePageId_SkipsEveryExistingId()
    {
        var pages = Pages("page-1", "page-2", "page-3");

        Assert.Equal("page-4", PageListOperations.CreateUniquePageId(pages));
    }

    private static IReadOnlyList<PageConfig> Pages(params string[] ids) =>
        [.. ids.Select(id => new PageConfig { Id = id, Widgets = [] })];
}
