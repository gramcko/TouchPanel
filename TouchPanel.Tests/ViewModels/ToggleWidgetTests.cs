using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Toggle behavior (T-10, RF-034/RF-035/D-15): the initial state comes from configuration, each
/// completed tap flips the visual state exactly once and executes the action once per flip, the
/// state is runtime-only (a rebuilt view model resets to the config), and disabled toggles neither
/// flip nor execute.
/// </summary>
public sealed class ToggleWidgetTests
{
    [Fact]
    public void Constructor_InitialStateComesFromConfiguration()
    {
        Assert.True(new ToggleWidgetViewModel(CreateConfig(initialOn: true, new MuteActionConfig())).IsOn);
        Assert.False(new ToggleWidgetViewModel(CreateConfig(initialOn: false, new MuteActionConfig())).IsOn);
    }

    [Fact]
    public async Task ActivateAsync_OneCompletedTap_FlipsExactlyOnceAndExecutesOnce()
    {
        var executor = new RecordingActionExecutor();
        var action = new MuteActionConfig();
        var viewModel = new ToggleWidgetViewModel(CreateConfig(initialOn: false, action), executor);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsOn);
        var invocation = Assert.Single(executor.Invocations);
        Assert.Same(action, invocation.Action);
        Assert.Equal("toggle-1", invocation.WidgetId);
    }

    [Fact]
    public async Task ActivateAsync_TwoCompletedTaps_ReturnToTheInitialStateAndExecutePerFlip()
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new ToggleWidgetViewModel(CreateConfig(initialOn: true, new MuteActionConfig()), executor);

        await viewModel.ActivateAsync();
        Assert.False(viewModel.IsOn);

        await viewModel.ActivateAsync();
        Assert.True(viewModel.IsOn);

        Assert.Equal(2, executor.InvocationCount);
    }

    [Fact]
    public async Task RuntimeState_IsNeverPersistedAcrossViewModels()
    {
        var config = CreateConfig(initialOn: true, new MuteActionConfig());
        var executor = new RecordingActionExecutor();
        var first = new ToggleWidgetViewModel(config, executor);

        await first.ActivateAsync();
        Assert.False(first.IsOn);

        // D-15: a rebuild (for example after a restart) starts again from the configured state and
        // the configuration object itself was not mutated.
        var second = new ToggleWidgetViewModel(config, executor);
        Assert.True(second.IsOn);
        Assert.True(config.InitialOn);
    }

    [Fact]
    public async Task ActivateAsync_InEditMode_DoesNotFlipOrExecute()
    {
        // M2-RF-439: while editing, a toggle tap must not change its runtime state or run the
        // action (and therefore cannot surface a transient error).
        var executor = new RecordingActionExecutor();
        var viewModel = new ToggleWidgetViewModel(CreateConfig(initialOn: false, new MuteActionConfig()), executor);
        viewModel.SetEditMode(true);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsEditMode);
        Assert.False(viewModel.IsOn);
        Assert.Empty(executor.Invocations);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task ActivateAsync_AfterEditModeEnds_FlipsAndExecutesAgain()
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new ToggleWidgetViewModel(CreateConfig(initialOn: false, new MuteActionConfig()), executor);
        viewModel.SetEditMode(true);

        await viewModel.ActivateAsync();
        Assert.False(viewModel.IsOn);
        Assert.Equal(0, executor.InvocationCount);

        viewModel.SetEditMode(false);
        await viewModel.ActivateAsync();

        Assert.False(viewModel.IsEditMode);
        Assert.True(viewModel.IsOn);
        Assert.Equal(1, executor.InvocationCount);
    }

    [Fact]
    public async Task ActivateAsync_DisabledToggle_DoesNotFlipOrExecute()
    {
        var executor = new RecordingActionExecutor();
        var config = CreateConfig(initialOn: false, new MuteActionConfig());
        config.IsEnabled = false;
        config.DisabledReason = "Widget binds an unsupported action type and was disabled.";
        var viewModel = new ToggleWidgetViewModel(config, executor);

        await viewModel.ActivateAsync();

        Assert.False(viewModel.IsOn);
        Assert.Empty(executor.Invocations);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task ActivateAsync_ActionFails_StillFlipsAndSurfacesTheError()
    {
        var executor = new RecordingActionExecutor
        {
            ResultFactory = (_, _) => ActionExecutionResult.Failed("nope"),
        };
        var viewModel = new ToggleWidgetViewModel(
            CreateConfig(initialOn: false, new MuteActionConfig()),
            executor,
            new FakeTimeProvider());

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsOn);
        Assert.True(viewModel.HasError);
        Assert.Equal("nope", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ActivateAsync_NoBoundAction_StillFlipsWithoutExecuting()
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new ToggleWidgetViewModel(CreateConfig(initialOn: false, action: null), executor);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsOn);
        Assert.Empty(executor.Invocations);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public void PersistState_RestoresThePersistedValueOverTheConfiguredInitialState()
    {
        var config = CreateConfig(initialOn: false, new MuteActionConfig());
        config.PersistState = true;

        var viewModel = new ToggleWidgetViewModel(
            config,
            pageId: "main",
            stateLookup: (page, widget) => page == "main" && widget == "toggle-1" ? true : null);

        Assert.True(viewModel.IsOn);
    }

    [Fact]
    public void PersistState_NoStoredValue_FallsBackToTheConfiguredInitialState()
    {
        var config = CreateConfig(initialOn: true, new MuteActionConfig());
        config.PersistState = true;

        var viewModel = new ToggleWidgetViewModel(
            config,
            pageId: "main",
            stateLookup: (_, _) => null);

        Assert.True(viewModel.IsOn);
    }

    [Fact]
    public async Task PersistState_Flip_RecordsTheNewStateForItsCompositeKey()
    {
        var config = CreateConfig(initialOn: false, new MuteActionConfig());
        config.PersistState = true;
        var writes = new List<(string Page, string Widget, bool IsOn)>();

        var viewModel = new ToggleWidgetViewModel(
            config,
            pageId: "main",
            stateChanged: (page, widget, isOn) => writes.Add((page, widget, isOn)));

        await viewModel.ActivateAsync();
        await viewModel.ActivateAsync();

        Assert.Equal(
            [("main", "toggle-1", true), ("main", "toggle-1", false)],
            writes);
    }

    [Fact]
    public async Task PersistStateFalse_IsRuntimeOnlyAndNeverConsultsTheStore()
    {
        var config = CreateConfig(initialOn: true, new MuteActionConfig());
        var lookups = 0;
        var writes = 0;

        var viewModel = new ToggleWidgetViewModel(
            config,
            pageId: "main",
            stateLookup: (_, _) =>
            {
                lookups++;
                return false;
            },
            stateChanged: (_, _, _) => writes++);

        Assert.True(viewModel.IsOn);
        Assert.Equal(0, lookups);

        await viewModel.ActivateAsync();

        Assert.False(viewModel.IsOn);
        Assert.Equal(0, writes);

        // A rebuild (restart) again starts from the configured state (D-15 preserved).
        Assert.True(new ToggleWidgetViewModel(config).IsOn);
    }

    [Fact]
    public async Task PersistState_WithoutAPageId_StaysRuntimeOnly()
    {
        var config = CreateConfig(initialOn: false, new MuteActionConfig());
        config.PersistState = true;
        var writes = 0;

        var viewModel = new ToggleWidgetViewModel(
            config,
            stateChanged: (_, _, _) => writes++);

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsOn);
        Assert.Equal(0, writes);
    }

    private static ToggleWidgetConfig CreateConfig(bool initialOn, ActionConfig? action) => new()
    {
        Id = "toggle-1",
        X = 8,
        Y = 12,
        Width = 96,
        Height = 48,
        Label = "Mute",
        InitialOn = initialOn,
        Action = action,
    };
}
