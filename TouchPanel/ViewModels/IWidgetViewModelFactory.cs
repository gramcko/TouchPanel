using TouchPanel.Config;

namespace TouchPanel.ViewModels;

/// <summary>
/// Creates the per-type view model for a validated widget (DD-6/RF-032). The production
/// implementation is <c>Widgets.WidgetRegistry</c>; <see cref="PageViewModel"/> depends only on
/// this contract so the ViewModels layer stays independent of the concrete widget registrations
/// while still building every page "via the registry" at composition time.
/// </summary>
public interface IWidgetViewModelFactory
{
    /// <summary>
    /// Creates the view model for one validated widget config. Never returns <see langword="null"/>:
    /// an unregistered widget type yields an unsupported/disabled placeholder view model instead of
    /// dropping the widget (RF-047/RF-079).
    /// </summary>
    /// <param name="config">The validated widget config to project.</param>
    /// <returns>The widget view model.</returns>
    WidgetViewModelBase Create(WidgetConfig config);

    /// <summary>
    /// Creates the view model for one validated widget config within a known page, so page-scoped
    /// behavior (such as persisted Toggle state keyed <c>"&lt;pageId&gt;/&lt;widgetId&gt;"</c>,
    /// RF-045) can be wired. The default implementation ignores <paramref name="pageId"/> and
    /// delegates to <see cref="Create(WidgetConfig)"/>, so existing factories stay valid.
    /// </summary>
    /// <param name="config">The validated widget config to project.</param>
    /// <param name="pageId">The owning page id, or <see langword="null"/> when the page is unknown.</param>
    /// <returns>The widget view model.</returns>
    WidgetViewModelBase Create(WidgetConfig config, string? pageId) => Create(config);
}
