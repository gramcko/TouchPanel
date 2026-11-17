using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Touch pages-management view model (user-requested pages management): add with unique ids,
/// rename, the last-page guard, confirmation, active-page fallback, edit-mode gating, and atomic
/// persistence through the real single-writer settings coordinator with every other section
/// preserved. No WPF, window, or hardware is involved.
/// </summary>
public sealed class PagesEditorViewModelTests
{
    [Fact]
    public async Task AddPage_PersistsAUniquePage_AndPreservesEveryOtherSection()
    {
        var context = Context.Create(twoPages: true, activePageId: "second");

        context.ViewModel.NewPageName = "Media";
        var added = await context.ViewModel.AddPageAsync();

        Assert.True(added, context.ViewModel.StatusMessage);
        Assert.Equal(1, context.Store.SaveCallCount);
        var saved = context.Store.LastSavedConfig!;
        Assert.Equal(3, saved.Pages.Count);
        Assert.Equal("second", saved.ActivePageId);
        Assert.Equal("Media", saved.Pages[^1].Name);
        Assert.DoesNotContain(saved.Pages.Take(2), page => page.Id == saved.Pages[^1].Id);

        // Pages management must not reset the root-level sections it does not edit.
        Assert.True(saved.Window.AlwaysOnTop);
        Assert.Single(saved.Hotkeys);
        Assert.Equal("light", saved.Appearance.Theme);
        Assert.Contains("active-page", context.Target.Steps);
        Assert.Equal(saved.ActivePageId, context.Config.ActivePageId);
        Assert.Equal(3, context.ViewModel.Pages.Count);
        Assert.Equal(saved.Pages[^1].Id, context.ViewModel.SelectedPageId);
    }

    [Fact]
    public async Task AddPage_WithoutAName_GeneratesTheFirstFreePageLabel()
    {
        var context = Context.Create(twoPages: true);

        context.ViewModel.NewPageName = "   ";
        Assert.True(await context.ViewModel.AddPageAsync());

        var saved = context.Store.LastSavedConfig!;
        Assert.Equal("Page 1", saved.Pages[^1].Name);
    }

    [Fact]
    public async Task RenameSelected_PersistsTheNameAndKeepsIdOrderAndWidgets()
    {
        var context = Context.Create(twoPages: true, activePageId: "second");
        var widgets = context.Config.Pages[1].Widgets;

        context.ViewModel.SelectPage("second");
        context.ViewModel.SelectedPageName = "  Dashboard  ";
        var renamed = await context.ViewModel.RenameSelectedAsync();

        Assert.True(renamed, context.ViewModel.StatusMessage);
        var saved = context.Store.LastSavedConfig!;
        Assert.Equal("Dashboard", saved.Pages[1].Name);
        Assert.Equal("second", saved.Pages[1].Id);
        Assert.Same(widgets, saved.Pages[1].Widgets);
        Assert.Equal("second", saved.ActivePageId);
        Assert.Equal("Dashboard", Assert.Single(context.ViewModel.Pages, page => page.Id == "second").DisplayName);
    }

    [Fact]
    public async Task RenameSelected_BlankName_IsRefusedWithoutSaving()
    {
        var context = Context.Create(twoPages: true);
        context.ViewModel.SelectPage("second");

        context.ViewModel.SelectedPageName = "   ";
        var renamed = await context.ViewModel.RenameSelectedAsync();

        Assert.False(renamed);
        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.True(context.ViewModel.StatusIsError);
    }

    [Fact]
    public async Task RemoveSelected_RequiresConfirmation_AndRemovesOnlyAfterConfirm()
    {
        var context = Context.Create(twoPages: true);
        context.ViewModel.SelectPage("second");

        context.ViewModel.RequestRemoveSelected();

        Assert.True(context.ViewModel.IsRemoveConfirmationVisible);
        Assert.Equal("Second", context.ViewModel.PendingRemovePageName);
        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.Equal(2, context.Config.Pages.Count);

        Assert.True(await context.ViewModel.ConfirmRemoveAsync());

        Assert.False(context.ViewModel.IsRemoveConfirmationVisible);
        Assert.Equal(1, context.Store.SaveCallCount);
        Assert.Single(context.Config.Pages);
        Assert.Equal("main", context.Config.ActivePageId);
        Assert.Single(context.ViewModel.Pages);
    }

    [Fact]
    public void RequestRemovePage_SelectsTheRowThatRequestedIt()
    {
        var context = Context.Create(twoPages: true);

        context.ViewModel.RequestRemovePage("second");

        Assert.Equal("second", context.ViewModel.SelectedPageId);
        Assert.True(context.ViewModel.IsRemoveConfirmationVisible);
        Assert.Equal("Second", context.ViewModel.PendingRemovePageName);
    }

    [Fact]
    public async Task RemoveActivePage_FallsBackToTheFirstPageAndExplainsIt()
    {
        var context = Context.Create(twoPages: true, activePageId: "second");
        context.ViewModel.SelectPage("second");

        context.ViewModel.RequestRemoveSelected();
        Assert.True(await context.ViewModel.ConfirmRemoveAsync());

        var saved = context.Store.LastSavedConfig!;
        Assert.Equal("main", saved.ActivePageId);
        Assert.Equal(["main"], saved.Pages.Select(page => page.Id));
        Assert.Equal("main", context.Config.ActivePageId);
        Assert.Contains("active page", context.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(context.ViewModel.Pages.Single().IsActive);
    }

    [Fact]
    public async Task LastPage_CannotBeRemoved()
    {
        var context = Context.Create(twoPages: false);

        context.ViewModel.RequestRemoveSelected();
        Assert.False(context.ViewModel.IsRemoveConfirmationVisible);
        Assert.False(await context.ViewModel.ConfirmRemoveAsync());

        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.Single(context.Config.Pages);
        Assert.True(context.ViewModel.StatusIsError);
    }

    [Fact]
    public async Task EditModeGate_RefusesStructuralChangesWithoutSaving()
    {
        var context = Context.Create(twoPages: true, canManage: () => false);

        Assert.False(context.ViewModel.CanManagePages);
        Assert.False(await context.ViewModel.AddPageAsync());
        Assert.False(await context.ViewModel.RenameSelectedAsync());

        context.ViewModel.SelectPage("second");
        context.ViewModel.RequestRemoveSelected();
        Assert.False(await context.ViewModel.ConfirmRemoveAsync());

        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.Equal(2, context.Config.Pages.Count);
        Assert.True(context.ViewModel.StatusIsError);
    }

    [Fact]
    public async Task SaveFailure_KeepsTheRunningPagesAndReportsAnError()
    {
        var context = Context.Create(twoPages: true);
        context.Store.SaveSucceeds = false;

        var added = await context.ViewModel.AddPageAsync();

        Assert.False(added);
        Assert.Equal(2, context.Config.Pages.Count);
        Assert.True(context.ViewModel.StatusIsError);
        Assert.NotNull(context.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task SwitchToSelected_UsesThePageSwitchServiceAndRefreshesTheActiveFlag()
    {
        var context = Context.Create(twoPages: true);
        context.ViewModel.SelectPage("second");

        Assert.True(await context.ViewModel.SwitchToSelectedAsync());

        Assert.Equal(["second"], context.Switch.Requests);
        Assert.True(context.ViewModel.Pages.Single(page => page.Id == "second").IsActive);
        Assert.Equal("second", context.Config.ActivePageId);
    }

    [Fact]
    public async Task MovePageDown_PersistsTheOrder_KeepsTheActivePageById_AndPreservesEverySection()
    {
        var context = Context.Create(twoPages: true, activePageId: "main");

        context.ViewModel.SelectPage("main");
        var moved = await context.ViewModel.MovePageAsync("main", 1);

        Assert.True(moved, context.ViewModel.StatusMessage);
        Assert.Equal(1, context.Store.SaveCallCount);
        var saved = context.Store.LastSavedConfig!;
        Assert.Equal(["second", "main"], saved.Pages.Select(page => page.Id));
        Assert.Equal("main", saved.ActivePageId);
        Assert.Equal("main", context.Config.ActivePageId);
        Assert.Same(context.Config.Pages[1].Widgets, saved.Pages[1].Widgets);
        Assert.True(saved.Window.AlwaysOnTop);
        Assert.Single(saved.Hotkeys);
        Assert.Equal("light", saved.Appearance.Theme);
        Assert.Contains("active-page", context.Target.Steps);

        // The active page stays active by id even though its index changed.
        Assert.Equal("main", context.ViewModel.Pages.Single(page => page.IsActive).Id);
        Assert.Equal(2, context.ViewModel.Pages.Single(page => page.Id == "main").Position);
        Assert.Equal("main", context.ViewModel.SelectedPageId);
    }

    [Fact]
    public async Task MovePageUp_SavesTheNewOrder()
    {
        var context = Context.Create(twoPages: true, activePageId: "second");

        var moved = await context.ViewModel.MovePageAsync("second", 0);

        Assert.True(moved, context.ViewModel.StatusMessage);
        var saved = context.Store.LastSavedConfig!;
        Assert.Equal(["second", "main"], saved.Pages.Select(page => page.Id));
        Assert.Equal("second", saved.ActivePageId);
        Assert.Equal("second", context.ViewModel.Pages.Single(page => page.IsActive).Id);
    }

    [Fact]
    public async Task MoveSelectedCommands_MoveTheSelectedPage_AndRefuseAtTheEnds()
    {
        var context = Context.Create(twoPages: true);

        context.ViewModel.SelectPage("main");
        Assert.False(context.ViewModel.CanMoveSelectedUp);
        Assert.True(context.ViewModel.CanMoveSelectedDown);
        Assert.False(await context.ViewModel.MoveSelectedUpAsync());

        Assert.True(await context.ViewModel.MoveSelectedDownAsync());
        Assert.Equal(["second", "main"], context.Store.LastSavedConfig!.Pages.Select(page => page.Id));
        Assert.True(context.ViewModel.CanMoveSelectedUp);
        Assert.False(context.ViewModel.CanMoveSelectedDown);
        Assert.False(await context.ViewModel.MoveSelectedDownAsync());

        Assert.Equal(1, context.Store.SaveCallCount);
    }

    [Fact]
    public async Task MovePage_ToTheSamePosition_IsANoOpThatDoesNotSave()
    {
        var context = Context.Create(twoPages: true);

        var moved = await context.ViewModel.MovePageAsync("main", 0);

        Assert.False(moved);
        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.False(context.ViewModel.StatusIsError);
        Assert.Contains("already in that position", context.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MovePage_UnknownPage_IsRefusedWithoutSaving()
    {
        var context = Context.Create(twoPages: true);

        var moved = await context.ViewModel.MovePageAsync("missing", 1);

        Assert.False(moved);
        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.True(context.ViewModel.StatusIsError);
    }

    [Fact]
    public async Task MovePage_IsRefusedWhileTheEditModeGateIsClosed()
    {
        var context = Context.Create(twoPages: true, canManage: () => false);

        Assert.False(context.ViewModel.CanMoveSelectedUp);
        Assert.False(context.ViewModel.CanMoveSelectedDown);
        Assert.False(await context.ViewModel.MovePageAsync("main", 1));
        Assert.False(await context.ViewModel.MoveSelectedDownAsync());

        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.Equal(["main", "second"], context.Config.Pages.Select(page => page.Id));
        Assert.True(context.ViewModel.StatusIsError);
    }

    [Fact]
    public async Task MovePage_SaveFailure_KeepsTheRunningOrder()
    {
        var context = Context.Create(twoPages: true);
        context.Store.SaveSucceeds = false;

        var moved = await context.ViewModel.MovePageAsync("main", 1);

        Assert.False(moved);
        Assert.Equal(["main", "second"], context.Config.Pages.Select(page => page.Id));
        Assert.True(context.ViewModel.StatusIsError);
    }

    [Fact]
    public async Task MovePage_KeepsEveryWidgetWithItsPage()
    {
        var context = Context.Create(twoPages: true);
        var mainWidgets = context.Config.Pages[0].Widgets;

        Assert.True(await context.ViewModel.MovePageAsync("main", 1));

        var saved = context.Store.LastSavedConfig!;
        var movedMain = saved.Pages.Single(page => page.Id == "main");
        Assert.Same(mainWidgets, movedMain.Widgets);
        Assert.Equal("label", Assert.Single(movedMain.Widgets).Id);
    }

    private sealed class Context
    {
        private Context(
            Holder holder,
            RecordingConfigStore store,
            RecordingPanelConfigApplyTarget target,
            SettingsCoordinator settings,
            FakePageSwitchService pageSwitch,
            PagesEditorViewModel viewModel)
        {
            Holder = holder;
            Store = store;
            Target = target;
            Settings = settings;
            Switch = pageSwitch;
            ViewModel = viewModel;
        }

        public Holder Holder { get; }

        public PanelConfig Config => Holder.Config;

        public RecordingConfigStore Store { get; }

        public RecordingPanelConfigApplyTarget Target { get; }

        public SettingsCoordinator Settings { get; }

        public FakePageSwitchService Switch { get; }

        public PagesEditorViewModel ViewModel { get; }

        public static Context Create(
            bool twoPages,
            string activePageId = "main",
            Func<bool>? canManage = null)
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

            var store = new RecordingConfigStore();
            var target = new RecordingPanelConfigApplyTarget();
            var holder = new Holder { Config = config };
            var settings = new SettingsCoordinator(
                () => holder.Config,
                store,
                new PanelConfigApplier(target),
                new FakeStartupRegistration(),
                updated => holder.Config = updated);

            var pageSwitch = new FakePageSwitchService();
            pageSwitch.Handler = requested =>
            {
                var resolved = holder.Config.Pages.Find(page => page.Id == requested)?.Id ?? holder.Config.Pages[0].Id;
                holder.Config.ActivePageId = resolved;
                return new PageSwitchResult { Changed = true, ResolvedPageId = resolved };
            };

            var viewModel = new PagesEditorViewModel(
                settings,
                pageSwitch,
                () => holder.Config,
                canManage);

            return new Context(holder, store, target, settings, pageSwitch, viewModel);
        }
    }

    private sealed class Holder
    {
        public required PanelConfig Config { get; set; }
    }
}
