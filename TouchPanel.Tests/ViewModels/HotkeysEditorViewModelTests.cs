using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Input;
using TouchPanel.Services.Config;
using TouchPanel.Services.Notifications;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Hotkeys;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Hotkey-editor view model (M3-RF-611..619/AC-M3-05): list/add/edit/remove commands, inline
/// validation and duplicate blocking through the shared model, save-through the single-writer
/// <see cref="ISettingsService"/> path with <see cref="SettingsChange.Hotkeys"/>, the service-
/// unavailable note, and the live re-apply hook invocation with the validated list. No real global
/// hotkey is registered (M3-RNF-133).
/// </summary>
public sealed class HotkeysEditorViewModelTests
{
    [Fact]
    public void Constructor_ListsTheConfiguredEntries()
    {
        var model = new HotkeyEditorModel([Hotkey("A", ["Ctrl"])]);

        var viewModel = Create(model);

        var entry = Assert.Single(viewModel.Entries);
        Assert.Equal("Ctrl+A", entry.Combination);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void SaveEntry_AddsANewEntryAndMarksDirty()
    {
        var viewModel = Create(new HotkeyEditorModel());
        viewModel.ModifierCtrl = true;
        viewModel.SelectedKey = "A";

        var result = viewModel.SaveEntry();

        Assert.True(result.Succeeded);
        Assert.Single(viewModel.Entries);
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.CanApply);
        Assert.Null(viewModel.ValidationError);
    }

    [Fact]
    public void SaveEntry_WithoutModifier_ShowsInlineValidationAndBlocks()
    {
        var viewModel = Create(new HotkeyEditorModel());
        viewModel.SelectedKey = "A";

        var result = viewModel.SaveEntry();

        Assert.False(result.Succeeded);
        Assert.Empty(viewModel.Entries);
        Assert.True(viewModel.HasValidationError);
        Assert.True(viewModel.StatusIsError);
    }

    [Fact]
    public void SaveEntry_DuplicateCombination_IsBlockedInline()
    {
        var viewModel = Create(new HotkeyEditorModel([Hotkey("A", ["Ctrl"])]));
        viewModel.NewDraft();
        viewModel.ModifierCtrl = true;
        viewModel.SelectedKey = "A";

        var result = viewModel.SaveEntry();

        Assert.False(result.Succeeded);
        Assert.Contains("already used", viewModel.ValidationError, StringComparison.OrdinalIgnoreCase);
        Assert.Single(viewModel.Entries);
    }

    [Fact]
    public void SaveEntry_WithExistingSelection_UpdatesInsteadOfAdding()
    {
        var viewModel = Create(new HotkeyEditorModel([Hotkey("A", ["Ctrl"], "hotkey-1")]));
        viewModel.SelectedEntry = viewModel.Entries[0];
        viewModel.ModifierCtrl = false;
        viewModel.ModifierShift = true;
        viewModel.SelectedKey = "S";

        var result = viewModel.SaveEntry();

        Assert.True(result.Succeeded);
        Assert.Single(viewModel.Entries);
        Assert.Equal("Shift+S", viewModel.Entries[0].Combination);
    }

    [Fact]
    public void RemoveSelected_RemovesTheEntry()
    {
        var viewModel = Create(new HotkeyEditorModel([Hotkey("A", ["Ctrl"])]));
        viewModel.SelectedEntry = viewModel.Entries[0];

        Assert.True(viewModel.RemoveSelected());

        Assert.Empty(viewModel.Entries);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public async Task Apply_WithInvalidEntries_DoesNotCallTheSettingsBoundary()
    {
        var settings = new FakeSettingsService();
        // A duplicate combination keeps the model invalid.
        var model = new HotkeyEditorModel([Hotkey("A", ["Ctrl"], "hotkey-1"), Hotkey("a", ["ctrl"], "hotkey-2")]);
        var viewModel = Create(model, settings);

        var applied = await viewModel.ApplyAsync();

        Assert.False(applied);
        Assert.Empty(settings.Changes);
        Assert.True(viewModel.StatusIsError);
    }

    [Fact]
    public async Task Apply_SavesTheValidatedListAndMarksClean()
    {
        var settings = new FakeSettingsService();
        var viewModel = Create(new HotkeyEditorModel(), settings);
        viewModel.ModifierAlt = true;
        viewModel.SelectedKey = "S";
        viewModel.SaveEntry();

        var applied = await viewModel.ApplyAsync();

        Assert.True(applied);
        var change = Assert.Single(settings.Changes);
        Assert.NotNull(change.Hotkeys);
        Assert.Equal("S", Assert.Single(change.Hotkeys!).Key);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.StatusIsError);
    }

    [Fact]
    public async Task Apply_SendsOnlyHotkeys_SoTheCoordinatorPreservesEveryOtherSection()
    {
        var settings = new FakeSettingsService();
        var viewModel = Create(new HotkeyEditorModel(), settings);
        viewModel.ModifierAlt = true;
        viewModel.SelectedKey = "S";
        viewModel.SaveEntry();

        await viewModel.ApplyAsync();

        // M3-RF-615: a hotkey edit carries no other section, so BuildUpdated keeps
        // appearance/window/startup/telemetry/monitor untouched.
        var change = Assert.Single(settings.Changes);
        Assert.NotNull(change.Hotkeys);
        Assert.Null(change.Appearance);
        Assert.Null(change.AlwaysOnTop);
        Assert.Null(change.StartWithWindows);
        Assert.Null(change.PollIntervalSeconds);
        Assert.Null(change.MonitorFallback);
        Assert.False(change.SetMonitorMatch);
    }

    [Fact]
    public async Task Apply_SaveFailure_KeepsDirtyEditsAndShowsFailure()
    {
        var settings = new FakeSettingsService
        {
            Result = SettingsApplyResult.Failure("settings.saveFailed", "The settings could not be saved."),
        };
        var viewModel = Create(new HotkeyEditorModel(), settings);
        viewModel.ModifierCtrl = true;
        viewModel.SelectedKey = "A";
        viewModel.SaveEntry();

        var applied = await viewModel.ApplyAsync();

        Assert.False(applied);
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.StatusIsError);
        Assert.Single(viewModel.Entries);
    }

    [Fact]
    public void UnavailableService_ShowsTheNoteWithoutCrashing()
    {
        var viewModel = Create(new HotkeyEditorModel([Hotkey("A", ["Ctrl"])]), hotkeysAvailable: false);

        Assert.False(viewModel.HotkeysAvailable);
        Assert.True(viewModel.HasUnavailableNote);
        Assert.NotNull(viewModel.UnavailableNote);
    }

    [Fact]
    public async Task Apply_WhenTheServiceIsUnavailable_SavesAndNotifiesOnce()
    {
        var settings = new FakeSettingsService();
        var notifier = new RecordingNotifier();
        var viewModel = Create(new HotkeyEditorModel(), settings, notifier, hotkeysAvailable: false);
        viewModel.ModifierCtrl = true;
        viewModel.SelectedKey = "A";
        viewModel.SaveEntry();

        var applied = await viewModel.ApplyAsync();

        Assert.True(applied);
        Assert.False(viewModel.IsDirty);
        var notification = Assert.Single(notifier.Notifications);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
    }

    [Fact]
    public async Task Apply_TriggersTheReapplyHookWithTheValidatedList()
    {
        // M3-RF-614: saving runs the shared pipeline, which re-registers the validated list through
        // IPanelConfigApplyTarget.ApplyHotKeys (the fake target records it).
        var store = new RecordingConfigStore();
        var target = new RecordingPanelConfigApplyTarget();
        var holder = new Holder(DefaultConfigGenerator.Create());
        var coordinator = new SettingsCoordinator(
            () => holder.Config,
            store,
            new PanelConfigApplier(target),
            new FakeStartupRegistration(),
            updated => holder.Config = updated);

        var viewModel = Create(new HotkeyEditorModel(), coordinator);
        viewModel.ModifierCtrl = true;
        viewModel.SelectedKey = "A";
        viewModel.SaveEntry();

        var applied = await viewModel.ApplyAsync();

        Assert.True(applied);
        Assert.Contains("hotkeys", target.Steps);
        Assert.NotNull(target.LastHotkeys);
        Assert.Equal("A", Assert.Single(target.LastHotkeys!).Key);
    }

    [Fact]
    public void NewDraft_ClearsSelectionAndKeepsTheList()
    {
        var viewModel = Create(new HotkeyEditorModel([Hotkey("A", ["Ctrl"])]));
        viewModel.SelectedEntry = viewModel.Entries[0];

        viewModel.NewDraft();

        Assert.Null(viewModel.SelectedEntry);
        Assert.Single(viewModel.Entries);
        Assert.False(viewModel.ModifierCtrl);
    }

    private static HotkeyConfig Hotkey(string key, IReadOnlyList<string> modifiers, string id = "") => new()
    {
        Id = id,
        Key = key,
        Modifiers = [.. modifiers],
        Action = new MuteActionConfig(),
    };

    private static HotkeysEditorViewModel Create(
        HotkeyEditorModel model,
        ISettingsService? settings = null,
        IUserNotifier? notifier = null,
        bool hotkeysAvailable = true)
    {
        var (logger, _) = TestLoggerFactory.Create();
        return new HotkeysEditorViewModel(
            model,
            settings ?? new FakeSettingsService(),
            notifier ?? new RecordingNotifier(),
            hotkeysAvailable,
            reloadSource: null,
            logger);
    }

    private sealed class Holder(PanelConfig? config)
    {
        public PanelConfig? Config { get; set; } = config;
    }

    private sealed class RecordingNotifier : IUserNotifier
    {
        public List<NotificationRecord> Notifications { get; } = [];

        public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info) =>
            Notifications.Add(new NotificationRecord(title, message, severity));
    }

    private sealed record NotificationRecord(string Title, string Message, NotificationSeverity Severity);
}
