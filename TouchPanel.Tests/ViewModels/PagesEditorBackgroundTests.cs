using System.IO;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Edit;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Pages-editor background editing (user-requested addition): validated color/image drafts,
/// immediate clearing, live application through the shared single writer, per-page independence,
/// and preservation of the other sections, order, and widgets. No WPF, window, or hardware.
/// </summary>
public sealed class PagesEditorBackgroundTests
{
    [Fact]
    public async Task ApplyColor_PersistsOnlyTheSelectedPage_AndPreservesEverythingElse()
    {
        var context = Context.Create(twoPages: true, activePageId: "second");
        var widgets = context.Config.Pages[0].Widgets;

        context.ViewModel.SelectPage("second");
        context.ViewModel.BackgroundColor = "#102030";
        var applied = await context.ViewModel.ApplyBackgroundAsync();

        Assert.True(applied, context.ViewModel.StatusMessage);
        Assert.Equal(1, context.Store.SaveCallCount);
        var saved = context.Store.LastSavedConfig!;
        Assert.Equal("second", saved.ActivePageId);
        Assert.Equal(["main", "second"], saved.Pages.Select(page => page.Id));
        Assert.Equal("Second", saved.Pages[1].Name);
        Assert.Null(saved.Pages[0].Background);
        Assert.Equal("#102030", saved.Pages[1].Background);
        Assert.Same(widgets, saved.Pages[0].Widgets);

        Assert.True(saved.Window.AlwaysOnTop);
        Assert.Single(saved.Hotkeys);
        Assert.Equal("light", saved.Appearance.Theme);
        Assert.Contains("active-page", context.Target.Steps);

        // The running configuration and the editor drafts reflect the applied value.
        Assert.Equal("#102030", context.Config.Pages[1].Background);
        Assert.Equal("#102030", context.ViewModel.BackgroundColor);
        Assert.Equal(0, context.Store.LastSavedConfig!.SchemaVersion - PanelConfig.CurrentSchemaVersion);
    }

    [Fact]
    public async Task ApplyImage_PersistsTheAbsolutePath()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = TestPng.Write(temp.DirectoryPath, "wall.png");
            var context = Context.Create(twoPages: true);
            context.ViewModel.SelectPage("main");
            context.ViewModel.BackgroundImagePath = path;

            var applied = context.ViewModel.ApplyBackgroundAsync().GetAwaiter().GetResult();

            var saved = context.Store.LastSavedConfig!.Pages[0];
            return (
                Applied: applied,
                Status: context.ViewModel.StatusMessage,
                Image: saved.BackgroundImage,
                Color: saved.Background,
                Draft: context.ViewModel.BackgroundImagePath);
        });

        Assert.True(result.Applied, result.Status);
        Assert.True(Path.IsPathRooted(result.Image));
        Assert.Null(result.Color);
        Assert.Equal(result.Image, result.Draft);
    }

    [Fact]
    public async Task InvalidColor_IsRefusedWithoutSaving()
    {
        var context = Context.Create(twoPages: true);
        context.ViewModel.SelectPage("main");
        context.ViewModel.BackgroundColor = "green";

        var applied = await context.ViewModel.ApplyBackgroundAsync();

        Assert.False(applied);
        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.True(context.ViewModel.StatusIsError);
        Assert.Null(context.Config.Pages[0].Background);
    }

    [Theory]
    [InlineData(@"relative\wall.png")]
    [InlineData(@"C:\definitely-missing-touchpanel-bg.png")]
    public async Task InvalidImagePath_IsRefusedWithoutSaving(string path)
    {
        var context = Context.Create(twoPages: true);
        context.ViewModel.SelectPage("main");
        context.ViewModel.BackgroundImagePath = path;

        var applied = await context.ViewModel.ApplyBackgroundAsync();

        Assert.False(applied);
        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.True(context.ViewModel.StatusIsError);
        Assert.Null(context.Config.Pages[0].BackgroundImage);
    }

    [Fact]
    public async Task ClearColor_KeepsTheImage_AndClearImage_KeepsTheColor()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = TestPng.Write(temp.DirectoryPath, "wall.png");
            var context = Context.Create(twoPages: true);
            context.ViewModel.SelectPage("main");
            context.ViewModel.BackgroundColor = "#112233";
            context.ViewModel.BackgroundImagePath = path;
            var applied = context.ViewModel.ApplyBackgroundAsync().GetAwaiter().GetResult();

            var colorCleared = context.ViewModel.ClearBackgroundColorAsync().GetAwaiter().GetResult();
            var afterColorClear = context.Store.LastSavedConfig!.Pages[0];
            var imageCleared = context.ViewModel.ClearBackgroundImageAsync().GetAwaiter().GetResult();
            var afterImageClear = context.Store.LastSavedConfig!.Pages[0];

            return (
                Applied: applied,
                ColorCleared: colorCleared,
                ImageCleared: imageCleared,
                ColorAfterColorClear: afterColorClear.Background,
                ImageAfterColorClear: afterColorClear.BackgroundImage,
                ColorAfterImageClear: afterImageClear.Background,
                ImageAfterImageClear: afterImageClear.BackgroundImage,
                Saves: context.Store.SaveCallCount);
        });

        Assert.True(result.Applied);
        Assert.True(result.ColorCleared);
        Assert.True(result.ImageCleared);
        Assert.Null(result.ColorAfterColorClear);
        Assert.True(Path.IsPathRooted(result.ImageAfterColorClear));
        Assert.Null(result.ColorAfterImageClear);
        Assert.Null(result.ImageAfterImageClear);
        Assert.Equal(3, result.Saves);
    }

    [Fact]
    public async Task PagesAreIndependent()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = TestPng.Write(temp.DirectoryPath, "wall.png");
            var context = Context.Create(twoPages: true);

            context.ViewModel.SelectPage("main");
            context.ViewModel.BackgroundColor = "#010203";
            var firstApplied = context.ViewModel.ApplyBackgroundAsync().GetAwaiter().GetResult();

            context.ViewModel.SelectPage("second");
            var secondColorDraft = context.ViewModel.BackgroundColor;
            context.ViewModel.BackgroundImagePath = path;
            var secondApplied = context.ViewModel.ApplyBackgroundAsync().GetAwaiter().GetResult();

            var saved = context.Store.LastSavedConfig!;
            return (
                FirstApplied: firstApplied,
                SecondApplied: secondApplied,
                SecondColorDraft: secondColorDraft,
                Ids: saved.Pages.Select(page => page.Id).ToArray(),
                FirstColor: saved.Pages[0].Background,
                FirstImage: saved.Pages[0].BackgroundImage,
                SecondColor: saved.Pages[1].Background,
                SecondImage: saved.Pages[1].BackgroundImage,
                SecondName: saved.Pages[1].Name);
        });

        Assert.True(result.FirstApplied);
        Assert.True(result.SecondApplied);
        Assert.Equal(string.Empty, result.SecondColorDraft);
        Assert.Equal(["main", "second"], result.Ids);
        Assert.Equal("#010203", result.FirstColor);
        Assert.Null(result.FirstImage);
        Assert.Null(result.SecondColor);
        Assert.True(Path.IsPathRooted(result.SecondImage));
        Assert.Equal("Second", result.SecondName);
    }

    [Fact]
    public async Task Browse_UsesThePicker_AndAppliesImmediately()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = TestPng.Write(temp.DirectoryPath, "picked.png");
            var picker = new FakeImageFilePicker { Result = path };
            var context = Context.Create(twoPages: true, imagePicker: picker);
            context.ViewModel.SelectPage("second");

            var applied = context.ViewModel.BrowseBackgroundImageAsync().GetAwaiter().GetResult();
            return (Applied: applied, PickerCalls: picker.CallCount, SavedPath: context.Store.LastSavedConfig?.Pages[1].BackgroundImage);
        });

        Assert.True(result.Applied);
        Assert.Equal(1, result.PickerCalls);
        Assert.True(Path.IsPathRooted(result.SavedPath));
    }

    [Fact]
    public async Task BrowseCancel_ChangesNothing()
    {
        var picker = new FakeImageFilePicker { Result = null };
        var context = Context.Create(twoPages: true, imagePicker: picker);
        context.ViewModel.SelectPage("main");

        var applied = await context.ViewModel.BrowseBackgroundImageAsync();

        Assert.False(applied);
        Assert.Equal(1, picker.CallCount);
        Assert.Equal(0, context.Store.SaveCallCount);
    }

    [Fact]
    public async Task BackgroundEditing_IsGatedWhileEditModeOwnsThePanel()
    {
        var context = Context.Create(twoPages: true, canManage: () => false);
        context.ViewModel.SelectPage("main");
        context.ViewModel.BackgroundColor = "#102030";

        var applied = await context.ViewModel.ApplyBackgroundAsync();

        Assert.False(applied);
        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.True(context.ViewModel.StatusIsError);
        Assert.False(context.ViewModel.CanEditBackground);
    }

    [Fact]
    public void SelectingAPage_LoadsItsBackgroundDrafts()
    {
        var context = Context.Create(
            twoPages: true,
            configure: config =>
            {
                config.Pages[0].Background = "#AABBCC";
                config.Pages[1].BackgroundImage = @"C:\images\second.png";
            });

        context.ViewModel.SelectPage("main");
        Assert.Equal("#AABBCC", context.ViewModel.BackgroundColor);
        Assert.Equal(string.Empty, context.ViewModel.BackgroundImagePath);

        context.ViewModel.SelectPage("second");
        Assert.Equal(string.Empty, context.ViewModel.BackgroundColor);
        Assert.Equal(@"C:\images\second.png", context.ViewModel.BackgroundImagePath);
    }

    private sealed class Context
    {
        private Context(
            Holder holder,
            RecordingConfigStore store,
            RecordingPanelConfigApplyTarget target,
            PagesEditorViewModel viewModel)
        {
            Holder = holder;
            Store = store;
            Target = target;
            ViewModel = viewModel;
        }

        public Holder Holder { get; }

        public PanelConfig Config => Holder.Config;

        public RecordingConfigStore Store { get; }

        public RecordingPanelConfigApplyTarget Target { get; }

        public PagesEditorViewModel ViewModel { get; }

        public static Context Create(
            bool twoPages,
            string activePageId = "main",
            Func<bool>? canManage = null,
            Action<PanelConfig>? configure = null,
            IImageFilePicker? imagePicker = null)
        {
            var config = DefaultConfigGenerator.Create();
            config.Window.AlwaysOnTop = true;
            config.Appearance = new AppearanceConfig { Theme = "light", IconPack = "default" };
            config.Hotkeys =
            [
                new HotkeyConfig
                {
                    Id = "hk-a",
                    Key = "A",
                    Modifiers = ["Alt"],
                    Action = new MuteActionConfig(),
                },
            ];
            config.ActivePageId = activePageId;
            config.Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Name = "Main",
                    Widgets = [new LabelWidgetConfig { Id = "label", X = 0, Y = 0, Width = 100, Height = 40, Label = "Main" }],
                },
            ];
            if (twoPages)
            {
                config.Pages.Add(new PageConfig
                {
                    Id = "second",
                    Name = "Second",
                    Widgets = [],
                });
            }

            configure?.Invoke(config);

            var store = new RecordingConfigStore();
            var target = new RecordingPanelConfigApplyTarget();
            var holder = new Holder { Config = config };
            var settings = new SettingsCoordinator(
                () => holder.Config,
                store,
                new PanelConfigApplier(target),
                new FakeStartupRegistration(),
                updated => holder.Config = updated);

            var pageSwitch = new FakePageSwitchService
            {
                Handler = requested =>
                {
                    var resolved = holder.Config.Pages.Find(page => page.Id == requested)?.Id ?? holder.Config.Pages[0].Id;
                    holder.Config.ActivePageId = resolved;
                    return new PageSwitchResult { Changed = true, ResolvedPageId = resolved };
                },
            };

            var viewModel = new PagesEditorViewModel(
                settings,
                pageSwitch,
                () => holder.Config,
                canManage,
                imagePicker: imagePicker);

            return new Context(holder, store, target, viewModel);
        }
    }

    private sealed class Holder
    {
        public required PanelConfig Config { get; set; }
    }
}
