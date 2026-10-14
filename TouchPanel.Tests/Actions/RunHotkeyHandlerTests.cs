using TouchPanel.Actions;
using TouchPanel.Actions.Handlers;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Actions;

/// <summary>
/// Hotkey-reference execution (M5-RF-521/M5-RF-523): the resolver reads the live hotkey list
/// (first-wins for duplicate ids, blank/missing never resolve) and the handler dispatches exactly
/// the referenced hotkey's bound action through the injected <see cref="IActionExecutor"/> with no
/// chaining and no second execution surface.
/// </summary>
public sealed class RunHotkeyHandlerTests
{
    [Fact]
    public void Resolver_ResolvesById_FirstWinsAndRejectsBlankOrMissing()
    {
        var hotkeys = new List<HotkeyConfig>
        {
            new() { Id = "mute", Action = new MuteActionConfig() },
            new() { Id = "dup", Action = new SendKeysActionConfig { Key = "A" } },
            new() { Id = "dup", Action = new MediaKeyActionConfig { Key = MediaKeyKind.Next } },
        };
        var resolver = new HotkeyActionResolver(() => hotkeys);

        Assert.True(resolver.TryResolveAction("mute", out var mute));
        Assert.IsType<MuteActionConfig>(mute);

        Assert.True(resolver.TryResolveAction("dup", out var first));
        Assert.IsType<SendKeysActionConfig>(first);

        Assert.False(resolver.TryResolveAction("missing", out _));
        Assert.False(resolver.TryResolveAction("   ", out _));
        Assert.False(resolver.TryResolveAction(null, out _));
    }

    [Fact]
    public void Resolver_ReadsTheLiveList_SoLaterEditsApply()
    {
        var hotkeys = new List<HotkeyConfig> { new() { Id = "h1", Action = new MuteActionConfig() } };
        var resolver = new HotkeyActionResolver(() => hotkeys);

        hotkeys[0].Action = new VolumeActionConfig { Mode = VolumeMode.Set, Level = 20 };

        Assert.True(resolver.TryResolveAction("h1", out var action));
        var volume = Assert.IsType<VolumeActionConfig>(action);
        Assert.Equal(20, volume.Level);
    }

    [Fact]
    public async Task Handler_DispatchesTheResolvedActionThroughTheExecutor()
    {
        var recording = new RecordingActionExecutor();
        var resolver = new HotkeyActionResolver(() => new List<HotkeyConfig>
        {
            new() { Id = "h1", Action = new MediaKeyActionConfig { Key = MediaKeyKind.Next } },
        });
        var handler = new RunHotkeyHandler(resolver, recording);

        var result = await handler.ExecuteAsync(new RunHotkeyActionConfig { HotkeyId = "h1" }, CancellationToken.None);

        Assert.True(result.Success);
        var invocation = Assert.Single(recording.Invocations);
        Assert.IsType<MediaKeyActionConfig>(invocation.Action);
    }

    [Fact]
    public async Task Executor_RunsAHotkeyActionOnlyThroughTheInjectedExecutor()
    {
        var recording = new RecordingActionExecutor();
        var resolver = new HotkeyActionResolver(() => new List<HotkeyConfig>
        {
            new() { Id = "h1", Action = new MuteActionConfig() },
        });
        var executor = new ActionExecutor([new RunHotkeyHandler(resolver, recording)]);

        var result = await executor.ExecuteAsync(new RunHotkeyActionConfig { HotkeyId = "h1" }, widgetId: "b1");

        Assert.True(result.Success);
        Assert.Single(recording.Invocations);
    }

    [Fact]
    public async Task Handler_MissingReference_FailsGracefullyWithoutExecuting()
    {
        var recording = new RecordingActionExecutor();
        var handler = new RunHotkeyHandler(new HotkeyActionResolver(() => []), recording);

        var result = await handler.ExecuteAsync(new RunHotkeyActionConfig { HotkeyId = "missing" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(recording.Invocations);
    }

    [Fact]
    public async Task Handler_DoesNotChain_WhenTheTargetIsItselfAHotkeyReference()
    {
        var recording = new RecordingActionExecutor();
        var resolver = new HotkeyActionResolver(() => new List<HotkeyConfig>
        {
            new() { Id = "a", Action = new RunHotkeyActionConfig { HotkeyId = "b" } },
            new() { Id = "b", Action = new MuteActionConfig() },
        });
        var handler = new RunHotkeyHandler(resolver, recording);

        var result = await handler.ExecuteAsync(new RunHotkeyActionConfig { HotkeyId = "a" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(recording.Invocations);
    }

    [Fact]
    public async Task Handler_RejectsANonHotkeyAction()
    {
        var handler = new RunHotkeyHandler(new HotkeyActionResolver(() => []), new RecordingActionExecutor());

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.ExecuteAsync(new MuteActionConfig(), CancellationToken.None));
    }

    [Fact]
    public async Task Handler_DispatchesTheResolvedActionOffTheUiDispatcherThreadExactlyOnce()
    {
        // Regression: the nested hotkey-reference dispatch used to run on the caller's (UI) thread,
        // so a resolved send-keys action could hold modifiers while the dispatcher stalled. The
        // resolved action must be dispatched exactly once, off the UI dispatcher thread.
        using var sta = new StaThread();
        var dispatcher = await sta.InvokeAsync(() => System.Windows.Threading.Dispatcher.CurrentDispatcher);

        var onUiThread = new List<bool>();
        var recording = new RecordingActionExecutor { OnInvoke = _ => onUiThread.Add(dispatcher.CheckAccess()) };
        var resolver = new HotkeyActionResolver(() => new List<HotkeyConfig>
        {
            new() { Id = "h1", Action = new MuteActionConfig() },
        });
        var handler = new RunHotkeyHandler(resolver, recording);

        Task<ActionExecutionResult> execution = null!;
        await sta.InvokeAsync(() =>
        {
            execution = handler.ExecuteAsync(new RunHotkeyActionConfig { HotkeyId = "h1" }, CancellationToken.None);
        });
        var result = await execution;

        Assert.True(result.Success);
        Assert.Single(recording.Invocations);
        Assert.NotEmpty(onUiThread);
        Assert.All(onUiThread, value => Assert.False(value));
    }
}
