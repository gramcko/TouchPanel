using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Settings;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Page management from inside edit mode (user-requested): add, rename, remove, and open save the
/// pending layout, suspend the edit session, persist through the shared single-writer settings
/// coordinator (preserving every other section), and then re-enter edit mode. The last-page guard,
/// active-page fallback, unsaved-layout refusal, and the pass-through behavior outside edit mode
/// are covered without WPF or a window.
/// </summary>
public sealed class EditModePageManagementTests
{
    [Fact]
    public async Task AddPage_FromEditMode_SuspendsAppliesAndResumes_AndPreservesOtherSections()
    {
        var context = Context.Create(twoPages: true, activePageId: "second", editModeActive: true);

        context.ViewModel.NewPageName = "Media";
        var added = await context.ViewModel.AddPageAsync();

        Assert.True(added, context.ViewModel.StatusMessage);
        Assert.Equal(["suspend", "resume"], context.Gate.Steps);
        Assert.Equal(0, context.Gate.SaveCountAtSuspend);
        Assert.True(context.Gate.SaveCountAtResume > 0);
        Assert.Equal(1, context.Store.SaveCallCount);

        var saved = context.Store.LastSavedConfig!;
        Assert.Equal(3, saved.Pages.Count);
        Assert.Equal("Media", saved.Pages[^1].Name);
        Assert.Equal("second", saved.ActivePageId);
        Assert.True(saved.Window.AlwaysOnTop);
        Assert.Single(saved.Hotkeys);
        Assert.Equal("light", saved.Appearance.Theme);
        Assert.Contains("active-page", context.Target.Steps);
        Assert.Equal(3, context.ViewModel.Pages.Count);
    }

    [Fact]
    public async Task Rename_FromEditMode_KeepsIdOrderAndWidgets()
    {
        var context = Context.Create(twoPages: true, activePageId: "second", editModeActive: true);
        var widgets = context.Config.Pages[1].Widgets;

        context.ViewModel.SelectPage("second");
        context.ViewModel.SelectedPageName = "  Dashboard  ";
        var renamed = await context.ViewModel.RenameSelectedAsync();

        Assert.True(renamed, context.ViewModel.StatusMessage);
        Assert.Equal(["suspend", "resume"], context.Gate.Steps);

        var saved = context.Store.LastSavedConfig!;
        Assert.Equal("Dashboard", saved.Pages[1].Name);
        Assert.Equal("second", saved.Pages[1].Id);
        Assert.Same(widgets, saved.Pages[1].Widgets);
        Assert.Equal("second", saved.ActivePageId);
    }

    [Fact]
    public async Task RemoveActivePage_FromEditMode_FallsBackToTheFirstPage()
    {
        var context = Context.Create(twoPages: true, activePageId: "second", editModeActive: true);

        context.ViewModel.SelectPage("second");
        context.ViewModel.RequestRemoveSelected();
        Assert.True(context.ViewModel.IsRemoveConfirmationVisible);
        Assert.True(await context.ViewModel.ConfirmRemoveAsync());

        Assert.Equal(["suspend", "resume"], context.Gate.Steps);
        var saved = context.Store.LastSavedConfig!;
        Assert.Equal("main", saved.ActivePageId);
        Assert.Equal(["main"], saved.Pages.Select(page => page.Id));
        Assert.Equal("main", context.Config.ActivePageId);
    }

    [Fact]
    public async Task LastPageGuard_FromEditMode_RefusesWithoutSuspendingTheSession()
    {
        var context = Context.Create(twoPages: false, activePageId: "main", editModeActive: true);

        context.ViewModel.SelectPage("main");
        context.ViewModel.RequestRemoveSelected();
        Assert.False(context.ViewModel.IsRemoveConfirmationVisible);
        Assert.False(await context.ViewModel.ConfirmRemoveAsync());

        Assert.Empty(context.Gate.Steps);
        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.Single(context.Config.Pages);
        Assert.True(context.ViewModel.StatusIsError);
    }

    [Fact]
    public async Task OpenPage_FromEditMode_SuspendsSwitchesAndReentersOnTheNewPage()
    {
        var context = Context.Create(twoPages: true, activePageId: "main", editModeActive: true);

        context.ViewModel.SelectPage("second");
        var opened = await context.ViewModel.SwitchToSelectedAsync();

        Assert.True(opened, context.ViewModel.StatusMessage);
        Assert.Equal(["suspend", "resume"], context.Gate.Steps);
        Assert.Equal(0, context.Gate.SaveCountAtSuspend);
        Assert.True(context.Gate.SaveCountAtResume > 0);
        Assert.Equal("second", context.Config.ActivePageId);
        Assert.Equal("second", context.Store.LastSavedConfig!.ActivePageId);
        Assert.Contains("active-page", context.Target.Steps);
        Assert.True(context.ViewModel.Pages.Single(page => page.Id == "second").IsActive);
    }

    [Fact]
    public async Task UnsavedLayout_RefusesBothStructuralChangesAndOpening()
    {
        var context = Context.Create(twoPages: true, activePageId: "main", editModeActive: true);
        context.Gate.SuspendResult = false;

        var added = await context.ViewModel.AddPageAsync();
        Assert.False(added);
        Assert.Equal(2, context.Config.Pages.Count);
        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.Equal(1, context.Gate.SuspendCount);
        Assert.Equal(0, context.Gate.ResumeCount);
        Assert.True(context.ViewModel.StatusIsError);

        context.ViewModel.SelectPage("second");
        var opened = await context.ViewModel.SwitchToSelectedAsync();
        Assert.False(opened);
        Assert.Equal("main", context.Config.ActivePageId);
        Assert.Equal(0, context.Store.SaveCallCount);
        Assert.Equal(2, context.Gate.SuspendCount);
        Assert.Equal(0, context.Gate.ResumeCount);
        Assert.True(context.ViewModel.StatusIsError);
    }

    [Fact]
    public async Task OutsideEditMode_TheSameSurfaceIsAPassThrough()
    {
        var context = Context.Create(twoPages: true, activePageId: "main", editModeActive: false);

        context.ViewModel.NewPageName = "Media";
        var added = await context.ViewModel.AddPageAsync();

        Assert.True(added, context.ViewModel.StatusMessage);
        Assert.Empty(context.Gate.Steps);
        Assert.Equal(1, context.Store.SaveCallCount);
        Assert.Equal(3, context.Config.Pages.Count);
    }

    private sealed class Context
    {
        private Context(
            Holder holder,
            RecordingConfigStore store,
            RecordingPanelConfigApplyTarget target,
            FakeEditSessionGate gate,
            PagesEditorViewModel viewModel)
        {
            Holder = holder;
            Store = store;
            Target = target;
            Gate = gate;
            ViewModel = viewModel;
        }

        public Holder Holder { get; }

        public PanelConfig Config => Holder.Config;

        public RecordingConfigStore Store { get; }

        public RecordingPanelConfigApplyTarget Target { get; }

        public FakeEditSessionGate Gate { get; }

        public PagesEditorViewModel ViewModel { get; }

        public static Context Create(
            bool twoPages,
            string activePageId,
            bool editModeActive)
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
                config.Pages.Add(new PageConfig { Id = "second", Name = "Second", Widgets = [] });
            }

            var store = new RecordingConfigStore();
            var target = new RecordingPanelConfigApplyTarget();
            var holder = new Holder { Config = config };
            var gate = new FakeEditSessionGate { IsEditModeActive = editModeActive };
            gate.SaveCountAccessor = () => store.SaveCallCount;
            var settings = new SettingsCoordinator(
                () => holder.Config,
                store,
                new PanelConfigApplier(target),
                new FakeStartupRegistration(),
                updated => holder.Config = updated);

            // Mirrors the production switch guard: edits block switching until the session gate
            // suspends (exits) edit mode for the deliberate, already-saved page change.
            var pageSwitch = new PageSwitchCoordinator(
                () => holder.Config,
                store,
                target,
                () => !gate.IsEditModeActive);

            var adapter = new EditModePageChangeService(settings, pageSwitch, gate);
            var viewModel = new PagesEditorViewModel(
                adapter,
                adapter,
                () => holder.Config);

            return new Context(holder, store, target, gate, viewModel);
        }
    }

    private sealed class Holder
    {
        public required PanelConfig Config { get; set; }
    }

    private sealed class FakeEditSessionGate : IEditSessionGate
    {
        public bool IsEditModeActive { get; set; }

        public bool SuspendResult { get; set; } = true;

        public int SuspendCount { get; private set; }

        public int ResumeCount { get; private set; }

        public int SaveCountAtSuspend { get; private set; } = -1;

        public int SaveCountAtResume { get; private set; } = -1;

        public Func<int>? SaveCountAccessor { get; set; }

        public List<string> Steps { get; } = [];

        public Task<bool> SuspendAsync(CancellationToken cancellationToken = default)
        {
            SuspendCount++;
            Steps.Add("suspend");
            SaveCountAtSuspend = SaveCountAccessor?.Invoke() ?? -1;
            if (!SuspendResult)
            {
                return Task.FromResult(false);
            }

            IsEditModeActive = false;
            return Task.FromResult(true);
        }

        public Task ResumeAsync()
        {
            ResumeCount++;
            IsEditModeActive = true;
            SaveCountAtResume = SaveCountAccessor?.Invoke() ?? -1;
            Steps.Add("resume");
            return Task.CompletedTask;
        }
    }
}
