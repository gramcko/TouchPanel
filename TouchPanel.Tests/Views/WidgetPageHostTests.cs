using System.Windows.Controls;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Views;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Page-host level coverage for the add-widget flow (M5-RF-530/M5-RF-532/AC-M5-05). The delivered
/// tests asserted the overlay item count only; these materialize the real <see cref="WidgetPageHost"/>
/// bound to a <see cref="PageViewModel"/> and prove the host's <c>ItemsControl</c> actually
/// re-materializes when a palette add changes the widget collection (via the collection-change
/// notification, not a full rebuild), that the add persists through the single-writer store, and
/// that a view-model build failure rolls the config back. No window is shown and no hardware is
/// required; the XAML is instantiated on an STA thread.
/// </summary>
public sealed class WidgetPageHostTests
{
    private const string PageId = "main";

    [Fact]
    public async Task AddWidget_FromEditMode_ReMaterializesTheHostItemsImmediately()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var config = ConfigWith();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);

            var host = new WidgetPageHost { DataContext = page };
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var items = host.WidgetItemsForTest;
            var collectionChanged = 0;
            ((System.Collections.Specialized.INotifyCollectionChanged)items.Items).CollectionChanged +=
                (_, _) => collectionChanged++;

            var before = items.Items.Count;

            service.Enter(1024, 600);
            Assert.True(service.AddWidget(WidgetTypes.Button));
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return (
                before,
                after: items.Items.Count,
                collectionChanged,
                pageCount: page.Widgets.Count,
                configCount: config.Pages[0].Widgets.Count,
                isDirty: service.IsDirty,
                selected: service.SelectedWidget is not null,
                hasEditor: service.HasPropertyEditor);
        });

        Assert.Equal(0, result.before);
        Assert.Equal(1, result.after);
        // The root-cause fix (M5-RF-532): the bound host observes a collection-change notification,
        // so its ItemsControl re-materializes instead of silently keeping a stale item list.
        Assert.Equal(1, result.collectionChanged);
        Assert.Equal(1, result.pageCount);
        Assert.Equal(1, result.configCount);
        Assert.True(result.isDirty);
        Assert.True(result.selected);
        Assert.True(result.hasEditor);
    }

    [Fact]
    public async Task AddWidget_ExitAsync_PersistsExactlyOneWidgetThroughTheStore()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var config = ConfigWith();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var store = new RecordingConfigStore();
            var service = new EditModeService(config, page, store, factory: registry, palette: registry);

            service.Enter(1024, 600);
            Assert.True(service.AddWidget(WidgetTypes.Button));

            var save = service.ExitAsync().GetAwaiter().GetResult();
            return (save, store.SaveCallCount, store.LastSavedConfig);
        });

        Assert.True(result.save.Succeeded);
        Assert.Equal(1, result.SaveCallCount);
        var saved = Assert.Single(result.LastSavedConfig!.Pages[0].Widgets);
        Assert.IsType<ButtonWidgetConfig>(saved);
    }

    [Fact]
    public async Task AddWidget_WhenTheFactoryThrows_RollsBackTheConfigAndPage()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var config = ConfigWith();
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            // Palette resolves the type, but materializing the view model fails (RF-012/RF-013).
            var service = new EditModeService(
                config,
                page,
                new RecordingConfigStore(),
                factory: new ThrowingFactory(),
                palette: registry);

            service.Enter(1024, 600);
            var added = service.AddWidget(WidgetTypes.Button);

            return (
                added,
                pageCount: page.Widgets.Count,
                configCount: config.Pages[0].Widgets.Count,
                isDirty: service.IsDirty);
        });

        Assert.False(result.added);
        Assert.Equal(0, result.pageCount);
        Assert.Equal(0, result.configCount);
        Assert.False(result.isDirty);
    }

    private static PanelConfig ConfigWith() => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = PageId,
        Pages = [new PageConfig { Id = PageId, Widgets = [] }],
    };

    /// <summary>Factory double that always fails so the add rollback path is exercised.</summary>
    private sealed class ThrowingFactory : IWidgetViewModelFactory
    {
        public WidgetViewModelBase Create(WidgetConfig config) =>
            throw new InvalidOperationException("Simulated widget view-model build failure.");
    }
}
