using TouchPanel.Services.Config;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IPageSwitchService"/> double (M2-RNF-124): records every requested page id
/// and returns a configurable <see cref="PageSwitchResult"/> so the page-selector view model can be
/// tested without WPF, a store, or a live apply target.
/// </summary>
internal sealed class FakePageSwitchService : IPageSwitchService
{
    /// <summary>Gets the requested page ids in call order.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Gets or sets the resolved page id returned when no <see cref="Handler"/> is set (defaults to the requested id).</summary>
    public string? ResolvedPageId { get; set; }

    /// <summary>Gets or sets whether the change is reported as applied when no <see cref="Handler"/> is set.</summary>
    public bool Changed { get; set; } = true;

    /// <summary>Gets or sets a per-call result/handler override (may throw to exercise containment).</summary>
    public Func<string, PageSwitchResult>? Handler { get; set; }

    /// <inheritdoc />
    public Task<PageSwitchResult> SwitchAsync(string requestedPageId, CancellationToken cancellationToken = default)
    {
        Requests.Add(requestedPageId);

        if (Handler is { } handler)
        {
            return Task.FromResult(handler(requestedPageId));
        }

        return Task.FromResult(new PageSwitchResult
        {
            Changed = Changed,
            ResolvedPageId = string.IsNullOrEmpty(ResolvedPageId) ? requestedPageId : ResolvedPageId,
        });
    }
}
