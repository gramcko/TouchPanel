using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Services.Processes;

namespace TouchPanel.Tests.Actions;

/// <summary>Records launch requests and returns a programmable result.</summary>
internal sealed class RecordingProcessLauncher : IProcessLauncher
{
    /// <summary>Gets or sets the result returned by every launch.</summary>
    public ProcessLaunchResult Result { get; set; } = ProcessLaunchResult.Ok();

    /// <summary>Gets the launch requests received, in order.</summary>
    public List<ProcessLaunchRequest> Requests { get; } = [];

    /// <inheritdoc />
    public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Result);
    }
}

/// <summary>Records virtual keys and returns a programmable injection outcome.</summary>
internal sealed class RecordingKeyInput : IKeyInput
{
    /// <summary>Gets or sets whether injection succeeds.</summary>
    public bool Succeed { get; set; } = true;

    /// <summary>Gets or sets the failure reason reported when <see cref="Succeed"/> is false.</summary>
    public string? Error { get; set; }

    /// <summary>Gets the virtual keys sent as full presses (key down + up), in order.</summary>
    public List<ushort> SentKeys { get; } = [];

    /// <summary>Gets the virtual keys held down by <see cref="TryKeyDown"/>, in order.</summary>
    public List<ushort> DownKeys { get; } = [];

    /// <summary>Gets the virtual keys released by <see cref="TryKeyUp"/>, in order.</summary>
    public List<ushort> UpKeys { get; } = [];

    /// <summary>Gets or sets a predicate that fails specific key-down events (for example the target key).</summary>
    public Func<ushort, bool>? KeyDownPredicate { get; set; }

    /// <summary>Gets or sets a predicate that fails specific key-up events.</summary>
    public Func<ushort, bool>? KeyUpPredicate { get; set; }

    /// <summary>
    /// Gets or sets a callback invoked for every synthesis call, on the calling thread, before the
    /// outcome is recorded. Used to prove input synthesis runs off the UI thread.
    /// </summary>
    public Action? OnKey { get; set; }

    /// <inheritdoc />
    public bool TrySendKey(ushort virtualKey, out string? error)
    {
        OnKey?.Invoke();
        SentKeys.Add(virtualKey);
        error = Succeed ? null : Error ?? "input blocked";
        return Succeed;
    }

    /// <inheritdoc />
    public bool TryKeyDown(ushort virtualKey, out string? error)
    {
        OnKey?.Invoke();
        DownKeys.Add(virtualKey);
        if (!Succeed || (KeyDownPredicate is { } predicate && !predicate(virtualKey)))
        {
            error = Error ?? "input blocked";
            return false;
        }

        error = null;
        return true;
    }

    /// <inheritdoc />
    public bool TryKeyUp(ushort virtualKey, out string? error)
    {
        OnKey?.Invoke();
        UpKeys.Add(virtualKey);
        if (!Succeed || (KeyUpPredicate is { } predicate && !predicate(virtualKey)))
        {
            error = Error ?? "input blocked";
            return false;
        }

        error = null;
        return true;
    }
}

/// <summary>Records absolute volume levels and returns a programmable outcome.</summary>
internal sealed class RecordingAudioController : IAudioController
{
    /// <summary>Gets or sets whether the level is applied.</summary>
    public bool Succeed { get; set; } = true;

    /// <summary>Gets or sets the failure reason reported when <see cref="Succeed"/> is false.</summary>
    public string? Error { get; set; }

    /// <summary>Gets the levels applied, in order.</summary>
    public List<int> SetLevels { get; } = [];

    /// <inheritdoc />
    public bool TrySetVolumeLevel(int level, out string? error)
    {
        SetLevels.Add(level);
        error = Succeed ? null : Error ?? "audio unavailable";
        return Succeed;
    }
}

/// <summary>Configurable <see cref="IActionHandler"/> double that records dispatches.</summary>
internal sealed class StubActionHandler : IActionHandler
{
    private readonly Func<ActionConfig, CancellationToken, Task<ActionExecutionResult>> _execute;

    /// <summary>Initializes a new instance of the <see cref="StubActionHandler"/> class.</summary>
    /// <param name="actionType">The action type this handler claims.</param>
    /// <param name="execute">The behavior; defaults to an immediate success.</param>
    public StubActionHandler(
        string actionType,
        Func<ActionConfig, CancellationToken, Task<ActionExecutionResult>>? execute = null)
    {
        ActionType = actionType;
        _execute = execute ?? ((_, _) => Task.FromResult(ActionExecutionResult.Succeeded()));
    }

    /// <inheritdoc />
    public string ActionType { get; }

    /// <summary>Gets the number of times the handler was invoked.</summary>
    public int InvocationCount { get; private set; }

    /// <summary>Gets the action passed to the most recent invocation.</summary>
    public ActionConfig? LastAction { get; private set; }

    /// <inheritdoc />
    public Task<ActionExecutionResult> ExecuteAsync(ActionConfig action, CancellationToken cancellationToken)
    {
        InvocationCount++;
        LastAction = action;
        return _execute(action, cancellationToken);
    }
}
