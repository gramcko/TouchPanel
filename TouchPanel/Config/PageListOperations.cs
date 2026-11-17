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
/// Pure page-list operations for the touch pages editor (user-requested pages management): create a
/// page with a unique id and a sensible default name, rename a page, remove a page with the
/// last-page guard, and resolve the active page deterministically when the active page is removed.
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
                ? new PageConfig { Id = pages[cursor].Id, Name = normalized, Widgets = pages[cursor].Widgets }
                : pages[cursor];
        }

        updated = result;
        normalizedName = normalized;
        return true;
    }

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
