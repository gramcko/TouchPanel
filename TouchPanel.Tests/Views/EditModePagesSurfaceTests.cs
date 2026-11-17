using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Settings;
using TouchPanel.Views;
using TouchPanel.Views.Settings;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Edit-mode pages surface (user-requested): the toolbar's touch-sized Pages toggle opens the
/// existing pages editor inside the overlay, binds the supplied view model, and remains inside the
/// edit-mode root so normal mode never shows it or takes focus. The view is instantiated on an STA
/// thread but never shown.
/// </summary>
public sealed class EditModePagesSurfaceTests
{
    [Fact]
    public async Task PagesToggle_OpensTheBoundEditorOnlyInsideEditMode()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithOneWidget();
            var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());
            var service = new EditModeService(config, page, new RecordingConfigStore());
            var pages = new PagesEditorViewModel(
                new FakeSettingsService(),
                new FakePageSwitchService(),
                () => config);

            overlay.DataContext = service;
            overlay.AttachPagesEditor(pages);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var root = Assert.IsType<Grid>(overlay.FindName("OverlayRoot"));
            var toggle = Assert.IsType<ToggleButton>(overlay.FindName("PagesToggleButton"));
            var panel = Assert.IsType<Border>(overlay.FindName("PagesPanel"));
            var editor = Assert.IsType<PagesEditorView>(overlay.FindName("PagesEditor"));
            var close = Assert.IsType<Button>(overlay.FindName("ClosePagesButton"));

            var rootInNormalMode = root.Visibility;
            var panelBeforeToggle = panel.Visibility;

            service.Enter(1024, 600);
            toggle.IsChecked = true;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var rootInEditMode = root.Visibility;
            var panelOpen = panel.Visibility;
            var boundEditor = ReferenceEquals(editor.DataContext, pages);

            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var panelAfterClose = panel.Visibility;

            service.ExitAsync().GetAwaiter().GetResult();

            return new
            {
                RootInNormalMode = rootInNormalMode,
                PanelBeforeToggle = panelBeforeToggle,
                RootInEditMode = rootInEditMode,
                PanelOpen = panelOpen,
                BoundEditor = boundEditor,
                PanelAfterClose = panelAfterClose,
                ToggleFocusable = toggle.Focusable,
                ToggleMinHeight = toggle.MinHeight,
                CloseFocusable = close.Focusable,
            };
        });

        Assert.Equal(Visibility.Collapsed, result.RootInNormalMode);
        Assert.Equal(Visibility.Collapsed, result.PanelBeforeToggle);
        Assert.Equal(Visibility.Visible, result.RootInEditMode);
        Assert.Equal(Visibility.Visible, result.PanelOpen);
        Assert.True(result.BoundEditor);
        Assert.Equal(Visibility.Collapsed, result.PanelAfterClose);
        Assert.False(result.ToggleFocusable);
        Assert.True(result.ToggleMinHeight >= 40);
        Assert.False(result.CloseFocusable);
    }

    private static PanelConfig ConfigWithOneWidget() => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = "main",
        Pages =
        [
            new PageConfig
            {
                Id = "main",
                Widgets =
                [
                    new LabelWidgetConfig { Id = "label", X = 0, Y = 0, Width = 100, Height = 40, Label = "Main" },
                ],
            },
        ],
        Appearance = new AppearanceConfig { Theme = "dark" },
    };
}
