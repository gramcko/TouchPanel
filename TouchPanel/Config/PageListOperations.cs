using TouchPanel.Config.Validation;

namespace TouchPanel.Config;

/// <summary>
/// Outcome of adding one page to a page list (user-requested pages management). The pages are
/// returned as a new list so the caller can build a candidate configuration without mutating the
/// running one before the single-writer save succeeds.
/// </summary>
/// <param name="Pages">The new page list, with the added page last.</param>
/// <param name="AddedPage">The page that was created.</param>
public sealed record PageAddResult(IReadOnlyList<PageConfig> Pages, PageConfig AddedPage);

/// <summary>
/// Outcome of removing one page. The active page is resolved deterministically: removing the active
/// page falls back to the first remaining page (mirroring the load-time validation fallback), while
/// removing any other page keeps the current active id. Removing the last page is refused.
/// </summary>
public sealed record PageRemovalResult
{
    /// <summary>Gets a value indicating whether a page was removed.</summary>
    public required bool Removed { get; init; }

    /// <summary>Gets the resulting page list (the original order without the removed page, or the unchanged list when refused).</summary>
    public required IReadOnlyList<PageConfig> Pages { get; init; }

    /// <summary>Gets the active page id after the removal (never a removed id).</summary>
    public required string ActivePageId { get; init; }

    /// <summary>Gets a value indicating whether the removed page was active and the selection fell back to the first page.</summary>
    public bool ActivePageFellBack { get; init; }

    /// <summary>Gets a notification-safe reason when the removal was refused; otherwise <see langword="null"/>.</summary>
    public string? FailureReason { get; init; }
}

/// <summary>
/// Outcome of moving (reordering) one page. The active page is preserved by id, so a reorder never
/// changes which page is shown even when that page's index changes; when the configured active id is
/// missing, the deterministic first-page fallback is applied. A no-op (same position, unknown page,
/// or a single-page list) returns the original list and <see cref="Moved"/> = <see langword="false"/>.
/// </summary>
public sealed record PageMoveResult
{
    /// <summary>Gets a value indicating whether the page order changed.</summary>
    public required bool Moved { get; init; }

    /// <summary>Gets the resulting page list in the new order (the original list when nothing moved).</summary>
    public required IReadOnlyList<PageConfig> Pages { get; init; }

    /// <summary>Gets the active page id after the move (unchanged by id; first page when missing).</summary>
    public required string ActivePageId { get; init; }

    /// <summary>Gets the 0-based index the page occupied before the move, or -1 when it was unknown.</summary>
    public int FromIndex { get; init; }

    /// <summary>Gets the 0-based index the page occupies after the move, or -1 when it was unknown.</summary>
    public int ToIndex { get; init; }

    /// <summary>Gets a notification-safe reason when the move was refused; otherwise <see langword="null"/>.</summary>
    public string? FailureReason { get; init; }
}

/// <summary>
/// Pure page-list operations for the touch pages editor (user-requested pages management): create a
/// page with a unique id and a sensible default name, rename a page, remove a page with the
/// last-page guard, resolve the active page deterministically when the active page is removed, and
/// reorder pages while keeping the active page active by id.
/// </summary>
/// <remarks>
/// The helpers never touch a store, a window, or WPF. They return new lists/objects so a caller can
/// persist a candidate atomically through the single-writer settings path and only then publish it.
/// Page ids are unique by construction; display names are not required to be unique, but generated
/// defaults avoid collisions so the pager stays readable.
/// </remarks>
public static class PageListOperations
{
    /// <summary>Maximum page-name length accepted by the editor, matching the validator.</summary>
    public const int MaxPageNameLength = ConfigValidator.MaxPageNameLength;

    /// <summary>Base id used for generated pages (for example <c>page-2</c>).</summary>
    public const string DefaultIdBase = "page";

    /// <summary>Prefix used for generated page labels (for example <c>Page 2</c>).</summary>
    public const string DefaultNamePrefix = "Page";

    /// <summary>
    /// Gets the touch label for a page: its configured name when present, otherwise its id. Pages
    /// without a name keep the historical id-only presentation.
    /// </summary>
    /// <param name="page">The page to label; must not be <see langword="null"/>.</param>
    /// <returns>The display label.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page"/> is <see langword="null"/>.</exception>
    public static string DisplayName(PageConfig page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return string.IsNullOrWhiteSpace(page.Name) ? page.Id : page.Name!;
    }

    /// <summary>
    /// Creates a new page appended to <paramref name="pages"/> with a unique id and a sensible
    /// default name. A supplied name is normalized (trimmed/truncated); a blank name becomes
    /// <c>Page N</c> with the first free <c>N</c>.
    /// </summary>
    /// <param name="pages">The existing pages; must not be <see langword="null"/>.</param>
    /// <param name="name">The optional requested name.</param>
    /// <returns>The new list and the created page.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pages"/> is <see langword="null"/>.</exception>
    public static PageAddResult AddPage(IReadOnlyList<PageConfig> pages, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var page = new PageConfig
        {
            Id = CreateUniquePageId(pages),
            Name = TryNormalizeName(name, out var normalized) ? normalized : CreateUniquePageName(pages),
            Widgets = [],
        };

        var updated = new PageConfig[pages.Count + 1];
        for (var index = 0; index < pages.Count; index++)
        {
            updated[index] = pages[index];
        }

        updated[^1] = page;
        return new PageAddResult(updated, page);
    }

    /// <summary>
    /// Renames one page while preserving the order, the id, and the widget list. The name is
    /// normalized; blank or missing names are refused so a page can never become unlabeled by
    /// accident. The current active page is not changed.
    /// </summary>
    /// <param name="pages">The existing pages; must not be <see langword="null"/>.</param>
    /// <param name="pageId">The id of the page to rename.</param>
    /// <param name="name">The requested name.</param>
    /// <param name="updated">The new list, or the original list when the operation was refused.</param>
    /// <param name="normalizedName">The applied name when the operation succeeded.</param>
    /// <returns><see langword="true"/> when the page was found and the name was valid.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pages"/> is <see langword="null"/>.</exception>
    public static bool TryRenamePage(
        IReadOnlyList<PageConfig> pages,
        string pageId,
        string? name,
        out IReadOnlyList<PageConfig> updated,
        out string? normalizedName)
    {
        ArgumentNullException.ThrowIfNull(pages);

        updated = pages;
        normalizedName = null;

        if (!TryNormalizeName(name, out var normalized))
        {
            return false;
        }

        var index = FindIndex(pages, pageId);
        if (index < 0)
        {
            return false;
        }

        var result = new PageConfig[pages.Count];
        for (var cursor = 0; cursor < pages.Count; cursor++)
        {
            result[cursor] = cursor == index
                ? CopyWith(
                    pages[cursor],
                    name: normalized,
                    background: pages[cursor].Background,
                    backgroundImage: pages[cursor].BackgroundImage)
                : pages[cursor];
        }

        updated = result;
        normalizedName = normalized;
        return true;
    }

    /// <summary>
    /// Sets or clears the optional background color and image of one page, preserving the page id,
    /// name, widgets, order, and every other page. Values are stored as supplied (trimmed by the
    /// model); <see cref="ConfigValidator"/> normalizes them on the way through the single-writer
    /// settings coordinator, so an invalid value is cleared with a warning instead of failing the
    /// edit. An unknown page leaves the original list untouched.
    /// </summary>
    /// <param name="pages">The existing pages; must not be <see langword="null"/>.</param>
    /// <param name="pageId">The id of the page to edit.</param>
    /// <param name="background">The desired color text, or <see langword="null"/> to clear it.</param>
    /// <param name="backgroundImage">The desired absolute image path, or <see langword="null"/> to clear it.</param>
    /// <param name="updated">The new list, or the original list when the page was not found.</param>
    /// <returns><see langword="true"/> when the page was found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pages"/> is <see langword="null"/>.</exception>
    public static bool TrySetPageBackground(
        IReadOnlyList<PageConfig> pages,
        string pageId,
        string? background,
        string? backgroundImage,
        out IReadOnlyList<PageConfig> updated)
    {
        ArgumentNullException.ThrowIfNull(pages);

        updated = pages;

        var index = FindIndex(pages, pageId);
        if (index < 0)
        {
            return false;
        }

        var result = new PageConfig[pages.Count];
        for (var cursor = 0; cursor < pages.Count; cursor++)
        {
            result[cursor] = cursor == index
                ? CopyWith(
                    pages[cursor],
                    name: pages[cursor].Name,
                    background: background,
                    backgroundImage: backgroundImage)
                : pages[cursor];
        }

        updated = result;
        return true;
    }

    private static PageConfig CopyWith(
        PageConfig page,
        string? name,
        string? background,
        string? backgroundImage) => new()
    {
        Id = page.Id,
        Name = name,
        Background = background,
        BackgroundImage = backgroundImage,
        Widgets = page.Widgets,
    };

    /// <summary>
    /// Removes one page. The last page is never removed (a panel always needs an active page), and
    /// removing the active page falls back to the first remaining page. Removing an inactive page
    /// keeps the current active id.
    /// </summary>
    /// <param name="pages">The existing pages; must not be <see langword="null"/>.</param>
    /// <param name="pageId">The id of the page to remove.</param>
    /// <param name="activePageId">The currently active page id, or <see langword="null"/> when unknown.</param>
    /// <returns>The removal outcome; never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pages"/> is <see langword="null"/>.</exception>
    public static PageRemovalResult RemovePage(
        IReadOnlyList<PageConfig> pages,
        string pageId,
        string? activePageId)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var index = FindIndex(pages, pageId);
        if (index < 0)
        {
            return Refuse(pages, activePageId, "The selected page no longer exists.");
        }

        if (pages.Count <= 1)
        {
            return Refuse(pages, activePageId, "The last page cannot be removed.");
        }

        var updated = new List<PageConfig>(pages.Count - 1);
        for (var cursor = 0; cursor < pages.Count; cursor++)
        {
            if (cursor != index)
            {
                updated.Add(pages[cursor]);
            }
        }

        var removedWasActive = string.Equals(pages[index].Id, activePageId, StringComparison.Ordinal);
        var resolvedActive = removedWasActive ? updated[0].Id : ResolveActive(updated, activePageId);

        return new PageRemovalResult
        {
            Removed = true,
            Pages = updated,
            ActivePageId = resolvedActive,
            ActivePageFellBack = removedWasActive,
        };
    }

    /// <summary>
    /// Moves one page to <paramref name="targetIndex"/>, preserving every page object (and therefore
    /// every widget), the page count, and the active page <b>by id</b>. The target is clamped to the
    /// list bounds; dropping a page on its current index is a no-op that saves nothing. The last-page
    /// guard is inherently satisfied because a move never changes the page count.
    /// </summary>
    /// <param name="pages">The existing pages; must not be <see langword="null"/>.</param>
    /// <param name="pageId">The id of the page to move.</param>
    /// <param name="targetIndex">The desired 0-based destination index; clamped to the list bounds.</param>
    /// <param name="activePageId">The currently active page id, or <see langword="null"/> when unknown.</param>
    /// <returns>The move outcome; never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pages"/> is <see langword="null"/>.</exception>
    public static PageMoveResult MovePage(
        IReadOnlyList<PageConfig> pages,
        string pageId,
        int targetIndex,
        string? activePageId)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var index = FindIndex(pages, pageId);
        if (index < 0)
        {
            return RefuseMove(pages, activePageId, -1, "The selected page no longer exists.");
        }

        var destination = Math.Clamp(targetIndex, 0, pages.Count - 1);
        if (destination == index)
        {
            return new PageMoveResult
            {
                Moved = false,
                Pages = pages,
                ActivePageId = ResolveActive(pages, activePageId),
                FromIndex = index,
                ToIndex = index,
            };
        }

        var updated = new List<PageConfig>(pages.Count);
        for (var cursor = 0; cursor < pages.Count; cursor++)
        {
            if (cursor != index)
            {
                updated.Add(pages[cursor]);
            }
        }

        // Moving down removes before inserting, so the destination is the index in the shortened
        // list. Rebuilding by index keeps the result independent of the movement direction.
        updated.Insert(destination, pages[index]);

        return new PageMoveResult
        {
            Moved = true,
            Pages = updated,
            ActivePageId = ResolveActive(updated, activePageId),
            FromIndex = index,
            ToIndex = destination,
        };
    }

    /// <summary>
    /// Moves one page by a relative offset (for example -1 for "Move up" and +1 for "Move down").
    /// Out-of-range offsets are clamped to the ends, so moving the first page up is a no-op.
    /// </summary>
    /// <param name="pages">The existing pages; must not be <see langword="null"/>.</param>
    /// <param name="pageId">The id of the page to move.</param>
    /// <param name="offset">The signed number of positions to move.</param>
    /// <param name="activePageId">The currently active page id, or <see langword="null"/> when unknown.</param>
    /// <returns>The move outcome; never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pages"/> is <see langword="null"/>.</exception>
    public static PageMoveResult MovePageBy(
        IReadOnlyList<PageConfig> pages,
        string pageId,
        int offset,
        string? activePageId)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var index = FindIndex(pages, pageId);
        if (index < 0)
        {
            return RefuseMove(pages, activePageId, -1, "The selected page no longer exists.");
        }

        return MovePage(pages, pageId, index + offset, activePageId);
    }

    /// <summary>
    /// Creates a page id that is unique within <paramref name="pages"/>, using <c>page-N</c> with the
    /// first free positive <c>N</c>. The ids are compared ordinally, matching the validator and the
    /// switch coordinator.
    /// </summary>
    /// <param name="pages">The existing pages; must not be <see langword="null"/>.</param>
    /// <returns>The unique page id.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pages"/> is <see langword="null"/>.</exception>
    public static string CreateUniquePageId(IReadOnlyList<PageConfig> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            used.Add(page.Id);
        }

        for (var suffix = 1; ; suffix++)
        {
            var candidate = $"{DefaultIdBase}-{suffix}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Creates a "Page N" label whose <c>N</c> is the first positive integer not already used by a
    /// page name (case-insensitive), so generated labels stay distinct.
    /// </summary>
    /// <param name="pages">The existing pages; must not be <see langword="null"/>.</param>
    /// <returns>The generated label.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pages"/> is <see langword="null"/>.</exception>
    public static string CreateUniquePageName(IReadOnlyList<PageConfig> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            if (!string.IsNullOrWhiteSpace(page.Name))
            {
                used.Add(page.Name!);
            }
        }

        for (var suffix = 1; ; suffix++)
        {
            var candidate = $"{DefaultNamePrefix} {suffix}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Normalizes a requested page name: surrounding whitespace is trimmed, an over-long name is
    /// truncated to <see cref="MaxPageNameLength"/>, and a blank name is invalid.
    /// </summary>
    /// <param name="name">The requested name.</param>
    /// <param name="normalized">The normalized name when valid.</param>
    /// <returns><see langword="true"/> when the name is non-blank.</returns>
    public static bool TryNormalizeName(string? name, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        normalized = trimmed.Length > MaxPageNameLength ? trimmed[..MaxPageNameLength] : trimmed;
        return true;
    }

    private static PageRemovalResult Refuse(
        IReadOnlyList<PageConfig> pages,
        string? activePageId,
        string reason) => new()
    {
        Removed = false,
        Pages = pages,
        ActivePageId = ResolveActive(pages, activePageId),
        FailureReason = reason,
    };

    private static PageMoveResult RefuseMove(
        IReadOnlyList<PageConfig> pages,
        string? activePageId,
        int index,
        string reason) => new()
    {
        Moved = false,
        Pages = pages,
        ActivePageId = ResolveActive(pages, activePageId),
        FromIndex = index,
        ToIndex = index,
        FailureReason = reason,
    };

    private static string ResolveActive(IReadOnlyList<PageConfig> pages, string? activePageId)
    {
        if (pages.Count == 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(activePageId)
            && FindIndex(pages, activePageId) >= 0)
        {
            return activePageId;
        }

        return pages[0].Id;
    }

    private static int FindIndex(IReadOnlyList<PageConfig> pages, string? pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return -1;
        }

        for (var index = 0; index < pages.Count; index++)
        {
            if (string.Equals(pages[index].Id, pageId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
