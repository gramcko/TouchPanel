using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Input;
using TouchPanel.Services.Notifications;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Input;

/// <summary>
/// RF-058/RF-059 service behavior over the fake <see cref="IHotKeyRegistration"/> (M2-RNF-124):
/// per-entry registration, invalid/duplicate skipping, an OS refusal that skips and continues with
/// exactly one aggregated notification and no retry, re-registration without leaked registrations,
/// UI-thread marshalling, action dispatch, and clean disposal. No real system hotkey is captured.
/// </summary>
public sealed class HotKeyServiceTests
{
    private const ushort VkA = 0x41;
    private const ushort VkS = 0x53;

    [Fact]
    public void Apply_RegistersEachValidHotkeyInConfigurationOrder()
    {
        using var harness = Harness.Create();

        harness.Service.Apply([Config("A", "Ctrl"), Config("S", "Alt", "Shift")]);

        Assert.Equal(2, harness.Registration.Registrations.Count);
        Assert.Equal(new[] { 1, 2 }, harness.Registration.Registrations.Select(r => r.Id));
        Assert.Equal(VkA, harness.Registration.Registrations[0].VirtualKey);
        Assert.True(harness.Registration.Registrations[0].Modifiers.HasFlag(HotKeyModifiers.Control));
        Assert.Equal(VkS, harness.Registration.Registrations[1].VirtualKey);
        Assert.False(harness.Registration.Disposed);
        Assert.Empty(harness.Notifier.Notifications);
    }

    [Fact]
    public void Apply_InvalidEntries_AreSkippedAndTheRestContinue()
    {
        using var harness = Harness.Create();

        // First entry has no modifier (invalid, M2-RF-594); the second is valid.
        harness.Service.Apply([Config("A"), Config("S", "Ctrl")]);

        Assert.Equal(1, harness.Registration.RegisterCallCount);
        Assert.Single(harness.Registration.Registrations);
        Assert.Equal(VkS, harness.Registration.Registrations[0].VirtualKey);
    }

    [Fact]
    public void Apply_DuplicateCombination_FirstWinsAndLaterDuplicatesAreSkipped()
    {
        using var harness = Harness.Create();

        harness.Service.Apply([Config("A", "Ctrl"), Config("A", "ctrl"), Config("S", "Alt")]);

        // The duplicate never reaches the registration boundary; only A (first) and S register.
        Assert.Equal(2, harness.Registration.RegisterCallCount);
        Assert.Equal(new ushort[] { VkA, VkS }, harness.Registration.Registrations.Select(r => r.VirtualKey));
        Assert.Empty(harness.Notifier.Notifications);
    }

    [Fact]
    public void Apply_RegistrationFailure_SkipsItContinuesAndDoesNotRetry()
    {
        using var harness = Harness.Create();
        harness.Registration.FailedVirtualKeys.Add(VkA);

        harness.Service.Apply([Config("A", "Ctrl"), Config("S", "Alt")]);

        // One attempt per entry (no retry loop) and the second entry still registered.
        Assert.Equal(2, harness.Registration.RegisterCallCount);
        Assert.Single(harness.Registration.Registrations);
        Assert.Equal(VkS, harness.Registration.Registrations[0].VirtualKey);

        var notification = Assert.Single(harness.Notifier.Notifications);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
        Assert.Contains("Ctrl+A", notification.Message);
    }

    [Fact]
    public void Apply_MultipleFailures_ProduceExactlyOneAggregatedNotification()
    {
        using var harness = Harness.Create();
        harness.Registration.FailedVirtualKeys.Add(VkA);
        harness.Registration.FailedVirtualKeys.Add(VkS);

        harness.Service.Apply([Config("A", "Ctrl"), Config("S", "Alt")]);

        var notification = Assert.Single(harness.Notifier.Notifications);
        Assert.Contains("Ctrl+A", notification.Message);
        Assert.Contains("Alt+S", notification.Message);
    }

    [Fact]
    public void Apply_Twice_UnregistersThePreviousSetFirst_NoLeaks()
    {
        using var harness = Harness.Create();

        harness.Service.Apply([Config("A", "Ctrl"), Config("S", "Alt")]);
        var firstIds = harness.Registration.RegisteredIds.ToArray();

        harness.Service.Apply([Config("S", "Shift")]);

        // Every previously registered id was released before the new set registered.
        Assert.Equal(firstIds, harness.Registration.Unregistrations);
        Assert.Equal(new[] { 1 }, harness.Registration.RegisteredIds.OrderBy(id => id));
        Assert.Equal(VkS, harness.Registration.Registrations[^1].VirtualKey);
    }

    [Fact]
    public void UnregisterAll_ReleasesRegistrationsAndStopsDispatch()
    {
        using var harness = Harness.Create();
        harness.Service.Apply([Config("A", "Ctrl")]);

        harness.Service.UnregisterAll();

        Assert.Empty(harness.Registration.RegisteredIds);
        Assert.Equal(new[] { 1 }, harness.Registration.Unregistrations);

        harness.Registration.RaisePressed(1);
        Assert.Empty(harness.Executor.Invocations);
    }

    [Fact]
    public void Pressed_DispatchesTheBoundActionThroughTheExecutor()
    {
        using var harness = Harness.Create();
        harness.Service.Apply([Config("A", "Ctrl")]);

        harness.Registration.RaisePressed(1);

        var invocation = Assert.Single(harness.Executor.Invocations);
        Assert.IsType<MuteActionConfig>(invocation.Action);
    }

    [Fact]
    public void Pressed_UnknownId_DoesNothing()
    {
        using var harness = Harness.Create();
        harness.Service.Apply([Config("A", "Ctrl")]);

        harness.Registration.RaisePressed(99);

        Assert.Empty(harness.Executor.Invocations);
    }

    [Fact]
    public void Pressed_ExecutorThrows_FailureIsContained()
    {
        using var harness = Harness.Create();
        harness.Executor.ThrowOnExecute = new InvalidOperationException("boom");
        harness.Service.Apply([Config("A", "Ctrl")]);

        // Must not propagate out of the message-pump handler.
        harness.Registration.RaisePressed(1);

        Assert.Contains(1, harness.Registration.RegisteredIds);
    }

    [Fact]
    public void Dispose_UnregistersEverythingAndDisposesTheBoundary()
    {
        var harness = Harness.Create();
        harness.Service.Apply([Config("A", "Ctrl")]);

        harness.Service.Dispose();

        Assert.True(harness.Registration.Disposed);
        Assert.Empty(harness.Registration.RegisteredIds);
        Assert.Equal(new[] { 1 }, harness.Registration.Unregistrations);
    }

    [Fact]
    public void Apply_AfterDispose_IsANoOp()
    {
        var harness = Harness.Create();
        harness.Service.Dispose();

        harness.Service.Apply([Config("A", "Ctrl")]);
        harness.Service.UnregisterAll();

        Assert.Equal(0, harness.Registration.RegisterCallCount);
        Assert.Empty(harness.Registration.Unregistrations);
    }

    [Fact]
    public async Task Apply_RunsRegistrationOnTheMarshalledUiThread()
    {
        using var sta = new StaThread();
        var dispatcher = await sta.InvokeAsync(() => Dispatcher.CurrentDispatcher);

        using var harness = Harness.Create(action => dispatcher.Invoke(action));
        harness.Registration.ExpectedThreadCheck = () => dispatcher.CheckAccess();

        // Called from the xUnit thread; the service must marshal every registration to the UI thread.
        harness.Service.Apply([Config("A", "Ctrl"), Config("S", "Alt")]);

        Assert.Equal(2, harness.Registration.OnExpectedThread.Count);
        Assert.All(harness.Registration.OnExpectedThread, Assert.True);
    }

    [Fact]
    public void Apply_NoHotkeys_DoesNotRegisterButStaysUsable()
    {
        using var harness = Harness.Create();

        harness.Service.Apply([]);

        Assert.Equal(0, harness.Registration.RegisterCallCount);
        Assert.Empty(harness.Notifier.Notifications);

        harness.Service.Apply([Config("A", "Ctrl")]);
        Assert.Single(harness.Registration.RegisteredIds);
    }

    private static HotkeyConfig Config(string key, params string[] modifiers) => new()
    {
        Id = string.Empty,
        Key = key,
        Modifiers = [.. modifiers],
        Action = new MuteActionConfig(),
    };

    private sealed class Harness : IDisposable
    {
        private Harness(
            HotKeyService service,
            FakeHotKeyRegistration registration,
            RecordingActionExecutor executor,
            RecordingNotifier notifier)
        {
            Service = service;
            Registration = registration;
            Executor = executor;
            Notifier = notifier;
        }

        public HotKeyService Service { get; }

        public FakeHotKeyRegistration Registration { get; }

        public RecordingActionExecutor Executor { get; }

        public RecordingNotifier Notifier { get; }

        public static Harness Create(Action<Action>? marshal = null)
        {
            var (logger, _) = TestLoggerFactory.Create();
            var registration = new FakeHotKeyRegistration();
            var executor = new RecordingActionExecutor();
            var notifier = new RecordingNotifier();
            var service = new HotKeyService(
                registration,
                executor,
                notifier,
                logger,
                marshal ?? (action => action()));

            return new Harness(service, registration, executor, notifier);
        }

        public void Dispose() => Service.Dispose();
    }

    private sealed class RecordingNotifier : IUserNotifier
    {
        public List<NotificationRecord> Notifications { get; } = [];

        public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info) =>
            Notifications.Add(new NotificationRecord(title, message, severity));
    }

    private sealed record NotificationRecord(string Title, string Message, NotificationSeverity Severity);
}
