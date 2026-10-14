using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Action-picker behavior (T-14, RF-043/M2-RF-434): the picker is offered only for action-bearing
/// widgets, selecting a type binds a fresh default action, per-type parameter editors build the
/// typed action (with a browsable executable for run-command and key+modifier editing for
/// send-keys), invalid actions disable the widget with visible feedback, every accepted edit
/// live-binds the widget and marks the layout dirty, and the change persists through the existing
/// single-writer save-on-exit path. No dialog, file, hardware, or user profile is touched.
/// </summary>
public sealed class ActionPickerTests
{
    private const string PageId = "main";

    [Fact]
    public void ActionPicker_IsOfferedOnlyForActionBearingWidgets()
    {
        Assert.True(EditorFor(Button("b1")).HasActionPicker);
        Assert.True(EditorFor(Toggle("t1")).HasActionPicker);
        Assert.False(EditorFor(Label("l1")).HasActionPicker);
        Assert.False(EditorFor(Gauge("g1")).HasActionPicker);
        Assert.False(EditorFor(Slider("s1")).HasActionPicker);
        Assert.False(EditorFor(Sparkline("sp1")).HasActionPicker);
    }

    [Fact]
    public void SelectingAValidType_BindsTheActionEnablesTheWidgetAndMarksDirty()
    {
        var context = CreateContext(Button("b1"));
        context.EnterSelect();
        var picker = context.Picker;

        picker.SelectedActionOption = Option(picker, ActionTypes.Volume);

        Assert.IsType<VolumeActionConfig>(context.Config.Pages[0].Widgets[0].Action);
        Assert.True(context.Config.Pages[0].Widgets[0].IsEnabled);
        Assert.False(picker.HasValidationError);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void SelectingNone_DisablesTheWidgetWithReasonAndFeedback()
    {
        var context = CreateContext(Button("b1"));
        context.EnterSelect();
        var picker = context.Picker;

        picker.SelectedActionOption = Option(picker, ActionPickerViewModel.NoneType);

        Assert.Null(context.Config.Pages[0].Widgets[0].Action);
        Assert.False(context.Config.Pages[0].Widgets[0].IsEnabled);
        Assert.NotNull(context.Config.Pages[0].Widgets[0].DisabledReason);
        Assert.True(picker.HasValidationError);
        Assert.NotNull(picker.ValidationError);
    }

    [Fact]
    public void RunCommand_BrowseFillsThePathAndEnablesTheWidget()
    {
        var executablePicker = new FakeExecutablePicker { Result = @"C:\Tools\game.exe" };
        var context = CreateContext(Button("b1"), executablePicker);
        context.EnterSelect();
        var picker = context.Picker;

        // Choosing run command with the default (empty) path is invalid and disables the widget.
        picker.SelectedActionOption = Option(picker, ActionTypes.RunCommand);
        Assert.False(context.Config.Pages[0].Widgets[0].IsEnabled);
        Assert.True(picker.HasValidationError);

        picker.BrowseExecutableCommand.Execute(null);

        var action = Assert.IsType<RunCommandActionConfig>(context.Config.Pages[0].Widgets[0].Action);
        Assert.Equal(@"C:\Tools\game.exe", action.Path);
        Assert.Equal(@"C:\Tools\game.exe", picker.PathText);
        Assert.True(context.Config.Pages[0].Widgets[0].IsEnabled);
        Assert.False(picker.HasValidationError);
        Assert.Equal(1, executablePicker.CallCount);
    }

    [Fact]
    public void RunCommand_CancelledBrowseLeavesThePathAndWidgetUnchanged()
    {
        var executablePicker = new FakeExecutablePicker { Result = null };
        var context = CreateContext(Button("b1"), executablePicker);
        context.EnterSelect();
        var picker = context.Picker;
        picker.SelectedActionOption = Option(picker, ActionTypes.RunCommand);

        picker.BrowseExecutableCommand.Execute(null);

        Assert.Equal(1, executablePicker.CallCount);
        Assert.Empty(Assert.IsType<RunCommandActionConfig>(context.Config.Pages[0].Widgets[0].Action).Path);
        Assert.True(picker.HasValidationError);
    }

    [Fact]
    public void RunCommand_ArgumentsAreDiscretePerLineAndBlankWorkingDirectoryIsNone()
    {
        var context = CreateContext(Button("b1"), new FakeExecutablePicker { Result = "tool.exe" });
        context.EnterSelect();
        var picker = context.Picker;
        picker.SelectedActionOption = Option(picker, ActionTypes.RunCommand);
        picker.BrowseExecutableCommand.Execute(null);

        picker.ArgumentsText = "--file\r\nvalue with spaces\r\n\r\n$(calc)\r\n";
        picker.WorkingDirectoryText = "   ";

        var action = Assert.IsType<RunCommandActionConfig>(context.Config.Pages[0].Widgets[0].Action);
        Assert.Equal(["--file", "value with spaces", "$(calc)"], action.Arguments);
        Assert.Null(action.WorkingDirectory);
    }

    [Fact]
    public void SendKeys_KeyAndModifiersBuildTheCombinationInCanonicalOrder()
    {
        var context = CreateContext(Button("b1"));
        context.EnterSelect();
        var picker = context.Picker;
        picker.SelectedActionOption = Option(picker, ActionTypes.SendKeys);

        picker.KeyText = "a";
        picker.ModifierShift = true;
        picker.ModifierCtrl = true;
        picker.ModifierAlt = true;

        var action = Assert.IsType<SendKeysActionConfig>(context.Config.Pages[0].Widgets[0].Action);
        Assert.Equal("a", action.Key);
        Assert.Equal(["Ctrl", "Alt", "Shift"], action.Modifiers);
        Assert.True(context.Config.Pages[0].Widgets[0].IsEnabled);
        Assert.False(picker.HasValidationError);
    }

    [Fact]
    public void SendKeys_UnknownKeyDisablesTheWidgetWithFeedback()
    {
        var context = CreateContext(Button("b1"));
        context.EnterSelect();
        var picker = context.Picker;
        picker.SelectedActionOption = Option(picker, ActionTypes.SendKeys);

        picker.KeyText = "Launch";

        Assert.False(context.Config.Pages[0].Widgets[0].IsEnabled);
        Assert.True(picker.HasValidationError);
        Assert.Equal("Launch", picker.KeyText);
    }

    [Fact]
    public void MediaKeySelection_UpdatesTheConfig()
    {
        var context = CreateContext(Button("b1"));
        context.EnterSelect();
        var picker = context.Picker;
        picker.SelectedActionOption = Option(picker, ActionTypes.MediaKey);

        picker.SelectedMediaKey = MediaKeyKind.Next;

        Assert.Equal(MediaKeyKind.Next, Assert.IsType<MediaKeyActionConfig>(context.Config.Pages[0].Widgets[0].Action).Key);
    }

    [Fact]
    public void Volume_LevelIsClampedAndDirectionEditsApply()
    {
        var context = CreateContext(Button("b1"));
        context.EnterSelect();
        var picker = context.Picker;
        picker.SelectedActionOption = Option(picker, ActionTypes.Volume);

        picker.LevelText = "250";
        var volume = Assert.IsType<VolumeActionConfig>(context.Config.Pages[0].Widgets[0].Action);
        Assert.Equal(ConfigValidator.MaxVolumeLevel, volume.Level);
        Assert.Equal("100", picker.LevelText);

        picker.LevelText = "-10";
        Assert.Equal(ConfigValidator.MinVolumeLevel, volume.Level);
        Assert.Equal("0", picker.LevelText);

        picker.LevelText = "abc";
        Assert.True(picker.HasValidationError);

        picker.SelectedVolumeMode = VolumeMode.Step;
        picker.SelectedVolumeDirection = VolumeDirection.Down;
        Assert.Equal(VolumeMode.Step, volume.Mode);
        Assert.Equal(VolumeDirection.Down, volume.Direction);
        Assert.False(picker.IsVolumeSetMode);
        Assert.True(picker.IsVolumeStepMode);
    }

    [Fact]
    public async Task ActionChange_LiveBindsTheWidgetSoTheNextTapExecutesTheNewAction()
    {
        var executor = new RecordingActionExecutor();
        var context = CreateContext(Button("b1"), executor: executor);
        context.EnterSelect();
        var picker = context.Picker;
        var widget = context.Widgets[0];

        Assert.True(widget.IsInteractable);

        picker.SelectedActionOption = Option(picker, ActionPickerViewModel.NoneType);
        Assert.False(widget.IsInteractable);

        picker.SelectedActionOption = Option(picker, ActionTypes.Volume);
        Assert.True(widget.IsInteractable);

        // Actions do not fire while editing (M2-RF-439); leaving edit mode restores execution.
        await context.Service.ExitAsync();

        await widget.ActivateAsync();

        var invocation = Assert.Single(executor.Invocations);
        Assert.IsType<VolumeActionConfig>(invocation.Action);
    }

    [Fact]
    public async Task ActionChange_PersistsThroughTheSingleWriterOnExit()
    {
        var context = CreateContext(Button("b1"));
        context.EnterSelect();
        var picker = context.Picker;

        picker.SelectedActionOption = Option(picker, ActionTypes.SendKeys);
        picker.KeyText = "F5";
        picker.ModifierAlt = true;

        var result = await context.Service.ExitAsync();

        Assert.True(result.Succeeded);
        var saved = Assert.IsType<SendKeysActionConfig>(context.Store.LastSavedConfig!.Pages[0].Widgets[0].Action);
        Assert.Equal("F5", saved.Key);
        Assert.Equal(["Alt"], saved.Modifiers);
    }

    [Fact]
    public void RunCommand_ArgumentsStayDiscreteValuesWithoutBecomingAShellLine()
    {
        // The picker only carries typed action data; there is no free-form command string, so an
        // argument that looks like a secret stays a discrete value and never becomes a shell line.
        var context = CreateContext(Button("b1"), new FakeExecutablePicker { Result = "tool.exe" });
        context.EnterSelect();
        var picker = context.Picker;
        picker.SelectedActionOption = Option(picker, ActionTypes.RunCommand);
        picker.BrowseExecutableCommand.Execute(null);

        picker.ArgumentsText = "--token=supersecret";

        var action = Assert.IsType<RunCommandActionConfig>(context.Config.Pages[0].Widgets[0].Action);
        Assert.Single(action.Arguments);
        Assert.Equal("--token=supersecret", action.Arguments[0]);
    }

    [Fact]
    public void HotkeyOption_IsOfferedInTheCatalog()
    {
        var picker = EditorFor(Button("b1")).ActionPicker!;

        var option = Assert.Single(picker.ActionTypeOptions, item => item.Type == ActionTypes.Hotkey);
        Assert.Equal("Hotkey", option.DisplayName);
    }

    [Fact]
    public void HotkeyAction_ListsConfiguredHotkeysAndWritesTheReference()
    {
        var hotkeys = new List<HotkeyConfig>
        {
            new() { Id = "h1", Modifiers = ["Ctrl"], Key = "A", Action = new MuteActionConfig() },
        };
        var context = CreateContext(Button("b1"), hotkeys: hotkeys);
        context.EnterSelect();
        var picker = context.Picker;

        Assert.True(picker.HasHotkeys);
        Assert.False(picker.HasNoHotkeys);
        var hotkeyOption = Assert.Single(picker.HotkeyOptions);
        Assert.Equal("h1", hotkeyOption.Id);

        picker.SelectedActionOption = Option(picker, ActionTypes.Hotkey);
        picker.SelectedHotkey = hotkeyOption;

        var action = Assert.IsType<RunHotkeyActionConfig>(context.Config.Pages[0].Widgets[0].Action);
        Assert.Equal("h1", action.HotkeyId);
        Assert.True(context.Config.Pages[0].Widgets[0].IsEnabled);
        Assert.False(picker.HasValidationError);
    }

    [Fact]
    public void HotkeyAction_BlankReferenceDisablesTheWidgetWithFeedback()
    {
        var context = CreateContext(Button("b1"));
        context.EnterSelect();
        var picker = context.Picker;

        picker.SelectedActionOption = Option(picker, ActionTypes.Hotkey);

        Assert.True(picker.HasNoHotkeys);
        Assert.Empty(picker.HotkeyOptions);
        Assert.False(context.Config.Pages[0].Widgets[0].IsEnabled);
        Assert.True(picker.HasValidationError);
    }

    [Fact]
    public void HotkeyAction_RemovingTheReferencedHotkeyLeavesTheWidgetDisabledButPresent()
    {
        var hotkeys = new List<HotkeyConfig>
        {
            new() { Id = "h1", Modifiers = ["Ctrl"], Key = "A", Action = new MuteActionConfig() },
        };
        var context = CreateContext(Button("b1"), hotkeys: hotkeys);
        context.EnterSelect();
        var picker = context.Picker;
        picker.SelectedActionOption = Option(picker, ActionTypes.Hotkey);
        picker.SelectedHotkey = picker.HotkeyOptions.Single();
        Assert.True(context.Config.Pages[0].Widgets[0].IsEnabled);

        // The hotkey is removed later: a re-validation disables the widget without dropping it.
        context.Config.Hotkeys.Clear();
        context.Config.Pages[0].Widgets[0].IsEnabled = true;
        context.Config.Pages[0].Widgets[0].DisabledReason = null;
        ConfigValidator.ValidateWidgetAction(context.Config.Pages[0].Widgets[0], context.Config.Hotkeys);

        Assert.False(context.Config.Pages[0].Widgets[0].IsEnabled);
        Assert.Single(context.Config.Pages[0].Widgets);
    }

    private static ActionTypeOption Option(ActionPickerViewModel picker, string type) =>
        picker.ActionTypeOptions.First(option => string.Equals(option.Type, type, StringComparison.Ordinal));

    private static PropertyEditorViewModel EditorFor(WidgetConfig widget)
    {
        var context = CreateContext(widget);
        context.EnterSelect();
        return context.Editor;
    }

    private static TestContext CreateContext(
        WidgetConfig widget,
        IExecutablePicker? executablePicker = null,
        IActionExecutor? executor = null,
        IReadOnlyList<HotkeyConfig>? hotkeys = null)
    {
        var config = ConfigWith(widget, hotkeys);
        var registry = WidgetRegistry.CreateDefault(new WidgetCreationContext { ActionExecutor = executor });
        var page = PageViewModel.CreateForActivePage(config, registry);
        var store = new RecordingConfigStore();
        var service = new EditModeService(
            config,
            page,
            store,
            factory: registry,
            palette: registry,
            executablePicker: executablePicker);
        return new TestContext(config, page, store, service);
    }

    private static PanelConfig ConfigWith(WidgetConfig widget, IReadOnlyList<HotkeyConfig>? hotkeys = null) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = PageId,
        Hotkeys = hotkeys is null ? [] : [.. hotkeys],
        Pages = [new PageConfig { Id = PageId, Widgets = [widget] }],
    };

    private static ButtonWidgetConfig Button(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 160,
        Height = 120,
        Action = new MuteActionConfig(),
        Label = id,
    };

    private static ToggleWidgetConfig Toggle(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 160,
        Height = 120,
        Action = new MuteActionConfig(),
        Label = id,
    };

    private static LabelWidgetConfig Label(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 160,
        Height = 40,
        Label = id,
    };

    private static GaugeWidgetConfig Gauge(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 240,
        Height = 180,
        Sensor = "cpu.total",
        Minimum = 0,
        Maximum = 100,
        Label = id,
    };

    private static SliderWidgetConfig Slider(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 240,
        Height = 120,
        Minimum = 0,
        Maximum = 100,
        Step = 1,
        Value = 50,
        Action = new VolumeActionConfig { Mode = VolumeMode.Set, Level = 50 },
        Label = id,
    };

    private static SparklineWidgetConfig Sparkline(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 240,
        Height = 120,
        Sensor = "cpu.total",
        WindowSeconds = SparklineWidgetConfig.DefaultWindowSeconds,
        Style = SparklineStyle.Line,
        Label = id,
    };

    /// <summary>Convenience bundle for the service under test and its collaborators.</summary>
    private sealed record TestContext(
        PanelConfig Config,
        PageViewModel Page,
        RecordingConfigStore Store,
        EditModeService Service)
    {
        /// <summary>Gets the widget view models of the edited page.</summary>
        public IReadOnlyList<WidgetViewModelBase> Widgets => Page.Widgets;

        /// <summary>Gets the property editor for the first widget.</summary>
        public PropertyEditorViewModel Editor => Service.PropertyEditor!;

        /// <summary>Gets the action picker for the first widget.</summary>
        public ActionPickerViewModel Picker => Editor.ActionPicker!;

        /// <summary>Enters edit mode, selects the first widget, and opens its edit menu (UX fix #1).</summary>
        public void EnterSelect()
        {
            Service.Enter(1000, 500);
            Service.SelectWidget(Widgets[0]);
            Service.OpenPropertyEditor();
        }
    }
}
