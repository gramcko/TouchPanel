using System.Collections.ObjectModel;
using System.ComponentModel;
using TouchPanel.Config;

namespace TouchPanel.ViewModels;

/// <summary>
/// The rendered page of the panel (RF-032/RF-037): one widget view model per validated config
/// entry, in configuration order, built through the widget registry boundary
/// (<see cref="IWidgetViewModelFactory"/>). The host binds this to an <c>ItemsControl</c> with a
/// <c>Canvas</c> panel; geometry lives on the widget view models, so the host contains no
/// per-widget-instance logic (DD-6).
/// </summary>
/// <remarks>
/// Edit mode may add or remove a widget view model for the local remove/duplicate shortcuts
/// (RF-060/M2-RF-603); both operations raise a change notification for <see cref="Widgets"/> so the
/// host's <c>ItemsControl</c> follows the edit. The configuration list remains the source of truth
/// and is mutated by <see cref="EditModeService"/> in lockstep.
/// </remarks>
public sealed class PageViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ObservableCollection<WidgetViewModelBase> _widgets;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageViewModel"/> class from one validated page.
    /// </summary>
    /// <param name="page">The validated page config; must not be <see langword="null"/>.</param>
    /// <param name="factory">The widget registry boundary; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public PageViewModel(PageConfig page, IWidgetViewModelFactory factory)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(factory);

        PageId = page.Id;

        _widgets = new ObservableCollection<WidgetViewModelBase>();
        foreach (var widget in page.Widgets)
        {
            // RF-047/RF-079: unregistered or invalid widgets are neither dropped nor thrown;
            // the registry returns a disabled placeholder for unsupported types, and validation
            // already marked invalid-action widgets disabled on their own template.
            // T-08/RF-045: the page id scopes persisted Toggle state (composite key).
            _widgets.Add(factory.Create(widget, PageId));
        }

        Widgets = _widgets;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the id of the rendered page (the resolved <c>activePageId</c>).</summary>
    public string PageId { get; }

    /// <summary>
    /// Gets the widget view models in configuration order. The backing collection is an
    /// <see cref="ObservableCollection{T}"/> exposed as a read-only list, so add/remove raise
    /// <see cref="System.Collections.Specialized.INotifyCollectionChanged.CollectionChanged"/>
    /// (as well as <see cref="PropertyChanged"/> for this property) and a bound host such as
    /// <c>WidgetPageHost</c> re-materializes the change immediately without a full page rebuild
    /// (M5-RF-532).
    /// </summary>
    public IReadOnlyList<WidgetViewModelBase> Widgets { get; }

    /// <summary>
    /// Adds a widget view model (for example a duplicate) to the rendered page in configuration
    /// order. Must run on the UI thread; the caller keeps the page's config list in lockstep.
    /// </summary>
    /// <param name="widget">The widget view model to append; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="widget"/> is <see langword="null"/>.</exception>
    internal void Add(WidgetViewModelBase widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        _widgets.Add(widget);
        OnPropertyChanged(nameof(Widgets));
    }

    /// <summary>
    /// Removes and disposes a widget view model after an edit-mode removal. Unknown widgets are not
    /// required to be part of the list; a widget not on the page reports <see langword="false"/> and
    /// nothing changes. Must run on the UI thread.
    /// </summary>
    /// <param name="widget">The widget view model to remove; must not be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the widget was on the page and was removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="widget"/> is <see langword="null"/>.</exception>
    internal bool Remove(WidgetViewModelBase widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        if (!_widgets.Remove(widget))
        {
            return false;
        }

        widget.Dispose();
        OnPropertyChanged(nameof(Widgets));
        return true;
    }

    /// <summary>
    /// Builds the page the configuration marked active (RF-037): the page whose id matches
    /// <see cref="PanelConfig.ActivePageId"/>, falling back to the first page (and, defensively,
    /// to the generated default page) when the id is missing or invalid. Validation normally
    /// resolves this at load time; the view model repeats the resolution so it stays correct for
    /// programmatic callers and tests.
    /// </summary>
    /// <param name="config">The validated configuration; must not be <see langword="null"/>.</param>
    /// <param name="factory">The widget registry boundary; must not be <see langword="null"/>.</param>
    /// <returns>The active page view model.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public static PageViewModel CreateForActivePage(PanelConfig config, IWidgetViewModelFactory factory)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(factory);

        var page = config.Pages.Find(candidate =>
            string.Equals(candidate.Id, config.ActivePageId, StringComparison.Ordinal));

        page ??= config.Pages.Count > 0 ? config.Pages[0] : DefaultConfigGenerator.CreateDefaultPage();

        return new PageViewModel(page, factory);
    }

    /// <inheritdoc />
    /// <remarks>Releases the widget view models (transient error timers) without touching the configuration.</remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var widget in _widgets)
        {
            widget.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
