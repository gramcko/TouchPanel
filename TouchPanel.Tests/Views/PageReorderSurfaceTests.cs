using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Settings;
using TouchPanel.Views;
using TouchPanel.Views.Settings;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Drag-to-reorder is implemented once in the shared pages editor, so the Settings surface and the
/// edit-mode Pages surface expose exactly the same behavior (same control type, same view model,
/// same move commands, and the same drop caret). XAML is instantiated on an STA thread but never
/// shown; no UI automation is used.
/// </summary>
public sealed class PageReorderSurfaceTests
{
    [Fact]
    public async Task BothPagesSurfaces_HostTheSameDragReorderEditor()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var config = new PanelConfig
            {
                SchemaVersion = PanelConfig.CurrentSchemaVersion,
                ActivePageId = "main",
                Pages =
                [
                    new PageConfig { Id = "main", Widgets = [] },
                    new PageConfig { Id = "second", Name = "Second", Widgets = [] },
                ],
                Appearance = new AppearanceConfig { Theme = "dark" },
            };

            var pages = new PagesEditorViewModel(
                new FakeSettingsService(),
                new FakePageSwitchService(),
                () => config);

            var settingsWindow = new SettingsWindow(
                new SettingsViewModel(
                    new FakeSettingsService(),
                    () => config,
                    importExport: null,
                    new FakeConfigFilePicker(),
                    "panel.json"),
                pages: pages);

            var overlay = new EditOverlay();
            overlay.AttachPagesEditor(pages);

            try
            {
                settingsWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                var settingsEditor = Assert.IsType<PagesEditorView>(settingsWindow.FindName("PagesEditor"));
                var editEditor = Assert.IsType<PagesEditorView>(overlay.FindName("PagesEditor"));

                return new
                {
                    SameType = settingsEditor.GetType() == editEditor.GetType(),
                    SettingsDragEnabled = settingsEditor.IsReorderDragEnabled,
                    EditDragEnabled = editEditor.IsReorderDragEnabled,
                    SettingsBound = ReferenceEquals(settingsEditor.DataContext, pages),
                    EditBound = ReferenceEquals(editEditor.DataContext, pages),
                    SettingsHasList = settingsEditor.FindName("PageItems") is ItemsControl,
                    EditHasList = editEditor.FindName("PageItems") is ItemsControl,
                    SettingsHasCaret = settingsEditor.FindName("PageDropIndicator") is Border,
                    EditHasCaret = editEditor.FindName("PageDropIndicator") is Border,
                };
            }
            finally
            {
                settingsWindow.Close();
            }
        });

        Assert.True(result.SameType);
        Assert.True(result.SettingsDragEnabled);
        Assert.True(result.EditDragEnabled);
        Assert.True(result.SettingsBound);
        Assert.True(result.EditBound);
        Assert.True(result.SettingsHasList);
        Assert.True(result.EditHasList);
        Assert.True(result.SettingsHasCaret);
        Assert.True(result.EditHasCaret);
    }
}
