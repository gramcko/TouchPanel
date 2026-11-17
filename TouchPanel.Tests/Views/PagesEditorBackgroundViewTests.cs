using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Settings;
using TouchPanel.Views.Settings;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Pages-editor background controls (user-requested addition): the color and image fields plus
/// Apply/Browse/Clear affordances exist, meet the 40 DIP touch minimum, bind to the view model, and
/// reflect the selected page's drafts. XAML is instantiated on an STA thread but never shown.
/// </summary>
public sealed class PagesEditorBackgroundViewTests
{
    [Fact]
    public async Task BackgroundControls_AreTouchSizedAndBound()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateContext();
            var view = new PagesEditorView { DataContext = context.ViewModel };
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var color = Assert.IsType<TextBox>(view.FindName("BackgroundColorBox"));
            var image = Assert.IsType<TextBox>(view.FindName("BackgroundImageBox"));
            var apply = Assert.IsType<Button>(view.FindName("ApplyBackgroundButton"));
            var browse = Assert.IsType<Button>(view.FindName("BrowseBackgroundImageButton"));
            var clearColor = Assert.IsType<Button>(view.FindName("ClearBackgroundColorButton"));
            var clearImage = Assert.IsType<Button>(view.FindName("ClearBackgroundImageButton"));

            return new
            {
                ColorHeight = color.MinHeight,
                ImageHeight = image.MinHeight,
                ApplyHeight = apply.MinHeight,
                BrowseHeight = browse.MinHeight,
                ClearColorHeight = clearColor.MinHeight,
                ClearImageHeight = clearImage.MinHeight,
                ApplyCommand = ReferenceEquals(apply.Command, context.ViewModel.ApplyBackgroundCommand),
                BrowseCommand = ReferenceEquals(browse.Command, context.ViewModel.BrowseBackgroundImageCommand),
                ClearColorCommand = ReferenceEquals(clearColor.Command, context.ViewModel.ClearBackgroundColorCommand),
                ClearImageCommand = ReferenceEquals(clearImage.Command, context.ViewModel.ClearBackgroundImageCommand),
                Enabled = apply.IsEnabled,
                ColorText = color.Text,
                ImageText = image.Text,
            };
        });

        Assert.True(result.ColorHeight >= 40);
        Assert.True(result.ImageHeight >= 40);
        Assert.True(result.ApplyHeight >= 40);
        Assert.True(result.BrowseHeight >= 40);
        Assert.True(result.ClearColorHeight >= 40);
        Assert.True(result.ClearImageHeight >= 40);
        Assert.True(result.ApplyCommand);
        Assert.True(result.BrowseCommand);
        Assert.True(result.ClearColorCommand);
        Assert.True(result.ClearImageCommand);
        Assert.True(result.Enabled);
        Assert.Equal(string.Empty, result.ColorText);
        Assert.Equal(string.Empty, result.ImageText);
    }

    [Fact]
    public async Task BackgroundControls_ReflectAndUpdateTheSelectedPage()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateContext();
            context.Config.Pages[1].Background = "#AABBCC";
            context.ViewModel.Refresh();

            var view = new PagesEditorView { DataContext = context.ViewModel };
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            context.ViewModel.SelectPage("second");
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var color = Assert.IsType<TextBox>(view.FindName("BackgroundColorBox"));
            var loaded = color.Text;

            context.ViewModel.BackgroundColor = "#102030";
            var applied = context.ViewModel.ApplyBackgroundAsync().GetAwaiter().GetResult();
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            return (
                Loaded: loaded,
                Applied: applied,
                TextAfterApply: color.Text,
                Saved: context.Config.Pages[1].Background);
        });

        Assert.Equal("#AABBCC", result.Loaded);
        Assert.True(result.Applied);
        Assert.Equal("#102030", result.TextAfterApply);
        Assert.Equal("#102030", result.Saved);
    }

    private static Context CreateContext()
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

        var settings = new FakeSettingsService
        {
            Handler = change =>
            {
                if (change.Pages is { } pages)
                {
                    config.Pages = [.. pages];
                    config.ActivePageId = change.ActivePageId ?? config.ActivePageId;
                }

                return SettingsApplyResult.Success(config);
            },
        };

        var viewModel = new PagesEditorViewModel(
            settings,
            new FakePageSwitchService(),
            () => config,
            imagePicker: new FakeImageFilePicker());

        return new Context(config, viewModel);
    }

    private sealed record Context(PanelConfig Config, PagesEditorViewModel ViewModel);
}
