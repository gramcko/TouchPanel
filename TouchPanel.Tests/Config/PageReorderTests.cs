using TouchPanel.Config;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Pure page reordering (drag-to-reorder support): moving a page by target index or relative offset,
/// bounds clamping, no-op drops, and the active page staying active by id even when its index
/// changes. No WPF, store, or window is involved.
/// </summary>
public sealed class PageReorderTests
{
    [Fact]
    public void MovePage_Down_PlacesThePageAtTheTargetIndex()
    {
        var pages = Pages("main", "second", "third");

        var result = PageListOperations.MovePage(pages, "main", 2, "main");

        Assert.True(result.Moved);
        Assert.Equal(0, result.FromIndex);
        Assert.Equal(2, result.ToIndex);
        Assert.Equal(["second", "third", "main"], result.Pages.Select(page => page.Id));
        Assert.Equal("main", result.ActivePageId);
    }

    [Fact]
    public void MovePage_Up_PlacesThePageBeforeTheTarget()
    {
        var pages = Pages("main", "second", "third");

        var result = PageListOperations.MovePage(pages, "third", 0, "main");

        Assert.True(result.Moved);
        Assert.Equal(2, result.FromIndex);
        Assert.Equal(0, result.ToIndex);
        Assert.Equal(["third", "main", "second"], result.Pages.Select(page => page.Id));
    }

    [Fact]
    public void MovePage_AdjacentSwap_ExchangesTheNeighbours()
    {
        var pages = Pages("main", "second", "third");

        var down = PageListOperations.MovePage(pages, "main", 1, "main");
        var up = PageListOperations.MovePage(pages, "third", 1, "main");

        Assert.Equal(["second", "main", "third"], down.Pages.Select(page => page.Id));
        Assert.Equal(["main", "third", "second"], up.Pages.Select(page => page.Id));
    }

    [Fact]
    public void MovePage_ToTheSameIndex_IsANoOpThatKeepsTheOriginalList()
    {
        var pages = Pages("main", "second", "third");

        var result = PageListOperations.MovePage(pages, "second", 1, "main");

        Assert.False(result.Moved);
        Assert.Null(result.FailureReason);
        Assert.Same(pages, result.Pages);
        Assert.Equal(1, result.FromIndex);
        Assert.Equal(1, result.ToIndex);
        Assert.Equal("main", result.ActivePageId);
    }

    [Fact]
    public void MovePage_ClampsOutOfBoundsTargets()
    {
        var pages = Pages("main", "second", "third");

        var farDown = PageListOperations.MovePage(pages, "main", 99, "main");
        var farUp = PageListOperations.MovePage(pages, "third", -99, "main");

        Assert.Equal(["second", "third", "main"], farDown.Pages.Select(page => page.Id));
        Assert.Equal(["third", "main", "second"], farUp.Pages.Select(page => page.Id));
    }

    [Fact]
    public void MovePage_UnknownPage_IsRefusedWithoutChangingTheList()
    {
        var pages = Pages("main", "second");

        var result = PageListOperations.MovePage(pages, "missing", 0, "main");

        Assert.False(result.Moved);
        Assert.Same(pages, result.Pages);
        Assert.Equal(-1, result.FromIndex);
        Assert.NotNull(result.FailureReason);
        Assert.Equal("main", result.ActivePageId);
    }

    [Fact]
    public void MovePage_KeepsTheActivePageActiveById_WhenItsIndexChanges()
    {
        var pages = Pages("main", "second", "third");

        var result = PageListOperations.MovePage(pages, "second", 0, "third");

        Assert.True(result.Moved);
        Assert.Equal("third", result.ActivePageId);
        Assert.Contains(result.Pages, page => page.Id == "third");
        Assert.Equal(2, result.Pages.ToList().FindIndex(page => page.Id == "third"));
    }

    [Fact]
    public void MovePage_WithAMissingActiveId_FallsBackToTheFirstPage()
    {
        var pages = Pages("main", "second", "third");

        var result = PageListOperations.MovePage(pages, "third", 0, "missing");

        Assert.True(result.Moved);
        Assert.Equal("third", result.ActivePageId);
    }

    [Fact]
    public void MovePage_WithNoActiveId_FallsBackToTheFirstPage()
    {
        var pages = Pages("main", "second");

        var result = PageListOperations.MovePage(pages, "second", 0, activePageId: null);

        Assert.True(result.Moved);
        Assert.Equal("second", result.ActivePageId);
    }

    [Fact]
    public void MovePage_SinglePage_IsANoOp()
    {
        var pages = Pages("only");

        var result = PageListOperations.MovePage(pages, "only", 3, "only");

        Assert.False(result.Moved);
        Assert.Same(pages, result.Pages);
        Assert.Single(result.Pages);
    }

    [Fact]
    public void MovePageBy_MovesRelativePositions()
    {
        var pages = Pages("main", "second", "third");

        var down = PageListOperations.MovePageBy(pages, "main", 1, "main");
        var up = PageListOperations.MovePageBy(pages, "third", -1, "main");
        var atTop = PageListOperations.MovePageBy(pages, "main", -1, "main");
        var atEnd = PageListOperations.MovePageBy(pages, "third", 1, "main");

        Assert.Equal(["second", "main", "third"], down.Pages.Select(page => page.Id));
        Assert.Equal(["main", "third", "second"], up.Pages.Select(page => page.Id));
        Assert.False(atTop.Moved);
        Assert.False(atEnd.Moved);
    }

    [Fact]
    public void MovePage_PreservesEveryPageAndItsWidgets()
    {
        var mainWidgets = new List<WidgetConfig> { new LabelWidgetConfig { Id = "label" } };
        var thirdWidgets = new List<WidgetConfig> { new LabelWidgetConfig { Id = "clock" } };
        var pages = new[]
        {
            new PageConfig { Id = "main", Widgets = mainWidgets },
            new PageConfig { Id = "second", Widgets = [] },
            new PageConfig { Id = "third", Widgets = thirdWidgets },
        };

        var result = PageListOperations.MovePage(pages, "main", 2, "main");

        Assert.Equal(3, result.Pages.Count);
        Assert.Same(pages[0], result.Pages[2]);
        Assert.Same(mainWidgets, result.Pages[2].Widgets);
        Assert.Same(thirdWidgets, result.Pages[1].Widgets);
        Assert.Same(pages[1], result.Pages[0]);
    }

    [Fact]
    public void MovePage_DoesNotMutateTheSourceList()
    {
        var pages = Pages("main", "second", "third");

        _ = PageListOperations.MovePage(pages, "main", 2, "main");

        Assert.Equal(["main", "second", "third"], pages.Select(page => page.Id));
    }

    private static IReadOnlyList<PageConfig> Pages(params string[] ids) =>
        ids.Select(id => new PageConfig { Id = id, Widgets = [] }).ToArray();
}
