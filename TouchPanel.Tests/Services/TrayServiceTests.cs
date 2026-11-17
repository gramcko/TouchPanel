using Serilog;
using Serilog.Events;
using TouchPanel.Interop;
using TouchPanel.Services.Notifications;
using TouchPanel.Services.Tray;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Services;

/// <summary>
/// RF-005/RF-007/RF-013/RF-024 tray behavior against a fake icon and a manual
/// <see cref="TimeProvider"/>: registration lifecycle, bounded retry/backoff, Explorer-restart
/// recovery, menu model and command events, balloon delegation, and dispose semantics. No real
/// tray call is made; the native tray UI is verified manually in T-14/MV-07.
/// </summary>
public sealed class TrayServiceTests
{
    [Fact]
    public void Start_WhenRegistrationSucceeds_AddsIconWithConfiguredHandleAndTooltip()
    {
        var icon = new FakeTrayIcon();
        var (service, sink, _) = CreateService(icon);

        try
        {
            service.Start();

            Assert.True(service.IsRegistered);
            Assert.Equal(1, icon.AddCallCount);
            Assert.Equal((nint)42, icon.LastIconHandle);
            Assert.Equal("TouchPanel", icon.LastTooltip);
            Assert.Contains(
                sink.Events,
                logEvent => logEvent.Level == LogEventLevel.Information
                    && logEvent.RenderMessage().Contains("icon registered", StringComparison.Ordinal));
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void Start_CalledTwice_AddsTheIconOnlyOnce()
    {
        var icon = new FakeTrayIcon();
        var (service, _, _) = CreateService(icon);

        try
        {
            service.Start();
            service.Start();

            Assert.Equal(1, icon.AddCallCount);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void Start_WhenRegistrationFails_RetriesWithExponentialBackoffUntilSuccess()
    {
        var icon = new FakeTrayIcon();
        icon.FailAdds(2);
        var (service, sink, time) = CreateService(icon);

        try
        {
            service.Start();

            Assert.Equal(1, icon.AddCallCount);
            Assert.False(service.IsRegistered);

            // The first retry is due after 1 s; nothing fires before that.
            time.Advance(TimeSpan.FromMilliseconds(500));
            Assert.Equal(1, icon.AddCallCount);
            time.Advance(TimeSpan.FromMilliseconds(500));
            Assert.Equal(2, icon.AddCallCount);
            Assert.False(service.IsRegistered);

            // The second retry is due 2 s after the first one.
            time.Advance(TimeSpan.FromMilliseconds(1999));
            Assert.Equal(2, icon.AddCallCount);
            time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(3, icon.AddCallCount);
            Assert.True(service.IsRegistered);

            var retryDelays = sink.Events
                .Where(logEvent => logEvent.RenderMessage().Contains("scheduling retry", StringComparison.Ordinal))
                .Select(logEvent => Convert.ToInt64(ScalarProperty(logEvent, "DelayMs")))
                .ToArray();
            Assert.Equal(new long[] { 1000, 2000 }, retryDelays);
            Assert.Contains(
                sink.Events,
                logEvent => logEvent.Level == LogEventLevel.Information
                    && logEvent.RenderMessage().Contains("after 2 retries", StringComparison.Ordinal));
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void Start_WhenRegistrationKeepsFailing_StopsAfterBoundedAttemptsAndLogsEachFailure()
    {
        var icon = new FakeTrayIcon();
        icon.FailAdds(int.MaxValue);
        var policy = new TrayRegistrationRetryPolicy(maxAttempts: 3, initialDelay: TimeSpan.FromSeconds(1));
        var (service, sink, time) = CreateService(icon, policy);

        try
        {
            service.Start();
            Assert.Equal(1, icon.AddCallCount);

            // Delays: 1 s, 2 s, 4 s.
            time.Advance(TimeSpan.FromSeconds(1));
            time.Advance(TimeSpan.FromSeconds(2));
            time.Advance(TimeSpan.FromSeconds(4));

            Assert.Equal(4, icon.AddCallCount); // initial attempt + 3 retries
            Assert.False(service.IsRegistered);

            time.Advance(TimeSpan.FromMinutes(5));
            Assert.Equal(4, icon.AddCallCount); // bounded: no further attempts

            var retryWarnings = sink.Events
                .Where(logEvent => logEvent.Level == LogEventLevel.Warning
                    && logEvent.RenderMessage().Contains("scheduling retry", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(3, retryWarnings.Length);
            Assert.All(retryWarnings, warning => Assert.Equal(87L, Convert.ToInt64(ScalarProperty(warning, "Win32Error"))));

            var error = Assert.Single(sink.Events, logEvent => logEvent.Level == LogEventLevel.Error);
            Assert.Contains("after 4 attempts", error.RenderMessage(), StringComparison.Ordinal);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void Start_WhenRegistrationThrows_IsIsolatedAndRetried()
    {
        var icon = new FakeTrayIcon { ThrowOnAdd = true };
        var (service, sink, time) = CreateService(icon);

        try
        {
            service.Start();
            Assert.False(service.IsRegistered);

            icon.ThrowOnAdd = false;
            time.Advance(TimeSpan.FromSeconds(1));

            Assert.True(service.IsRegistered);
            Assert.Contains(
                sink.Events,
                logEvent => logEvent.Level == LogEventLevel.Warning
                    && logEvent.Exception is InvalidOperationException);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void TaskbarCreated_WhenReAddSucceeds_ReRegistersImmediately()
    {
        var icon = new FakeTrayIcon();
        var (service, sink, _) = CreateService(icon);

        try
        {
            service.Start();
            icon.RaiseTaskbarCreated();

            Assert.True(service.IsRegistered);
            Assert.Equal(1, icon.ReAddCallCount);
            Assert.Equal(1, icon.AddCallCount);
            Assert.Contains(
                sink.Events,
                logEvent => logEvent.RenderMessage().Contains("re-registered", StringComparison.Ordinal));
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void TaskbarCreated_WhenReAddFails_RestartsRetriesAndRecovers()
    {
        var icon = new FakeTrayIcon();
        var (service, _, time) = CreateService(icon);

        try
        {
            service.Start();
            icon.FailReAdds(1);

            icon.RaiseTaskbarCreated();

            Assert.False(service.IsRegistered);
            Assert.Equal(1, icon.ReAddCallCount);
            Assert.Equal(1, icon.AddCallCount);

            time.Advance(TimeSpan.FromSeconds(1));

            Assert.True(service.IsRegistered);
            Assert.Equal(2, icon.AddCallCount);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void TaskbarCreated_AfterRetriesExhausted_ResetsTheBoundedBudget()
    {
        var icon = new FakeTrayIcon();
        icon.FailAdds(int.MaxValue);
        var (service, sink, time) = CreateService(icon);

        try
        {
            service.Start();
            for (var exponent = 0; exponent < 5; exponent++)
            {
                time.Advance(TimeSpan.FromSeconds(1 << exponent)); // 1, 2, 4, 8, 16 s
            }

            Assert.Equal(6, icon.AddCallCount);
            Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Error);

            // Explorer restarted: the re-registration failure gets a fresh bounded budget.
            icon.FailAdds(0);
            icon.FailReAdds(1);
            icon.RaiseTaskbarCreated();
            Assert.Equal(1, icon.ReAddCallCount);

            time.Advance(TimeSpan.FromSeconds(1));

            Assert.True(service.IsRegistered);
            Assert.Equal(7, icon.AddCallCount);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Theory]
    [InlineData(TrayIcon.WmLButtonUp)]
    [InlineData(TrayIcon.WmRButtonUp)]
    public void MouseAction_ButtonRelease_RaisesMenuRequestedWithCoordinates(int message)
    {
        var icon = new FakeTrayIcon();
        var (service, _, _) = CreateService(icon);

        try
        {
            TrayMenuRequestedEventArgs? received = null;
            service.MenuRequested += (_, e) => received = e;

            icon.RaiseMouseAction(message, 320, 240);

            Assert.NotNull(received);
            Assert.Equal(320, received.X);
            Assert.Equal(240, received.Y);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Theory]
    [InlineData(TrayIcon.WmLButtonDown)]
    [InlineData(TrayIcon.WmLButtonDblClk)]
    [InlineData(TrayIcon.WmRButtonDown)]
    [InlineData(TrayIcon.WmRButtonDblClk)]
    public void MouseAction_NonMenuMessages_DoNotRaiseMenuRequested(int message)
    {
        var icon = new FakeTrayIcon();
        var (service, _, _) = CreateService(icon);

        try
        {
            var raised = false;
            service.MenuRequested += (_, _) => raised = true;

            icon.RaiseMouseAction(message, 1, 1);

            Assert.False(raised);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void MenuRequested_WhenOneHandlerThrows_TheOtherHandlersStillRunAndLog()
    {
        var icon = new FakeTrayIcon();
        var (service, sink, _) = CreateService(icon);

        try
        {
            TrayMenuRequestedEventArgs? received = null;
            service.MenuRequested += (_, _) => throw new InvalidOperationException("bad handler");
            service.MenuRequested += (_, e) => received = e;

            icon.RaiseMouseAction(TrayIcon.WmRButtonUp, 10, 20);

            Assert.NotNull(received);
            Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void GetMenuItems_ExposesTheM1CommandsInOrder()
    {
        var (service, _, _) = CreateService(new FakeTrayIcon());

        try
        {
            var items = service.GetMenuItems();

            Assert.Equal(
                new[] { TrayMenuCommand.ShowHidePanel, TrayMenuCommand.OpenConfigurationFolder, TrayMenuCommand.Exit },
                items.Select(item => item.Command));
            Assert.Equal(
                new[] { "Show/Hide Panel", "Open Configuration Folder", "Exit" },
                items.Select(item => item.Text));
            Assert.All(items, item =>
            {
                Assert.True(item.IsEnabled);
                Assert.False(item.IsCheckable);
                Assert.False(item.IsChecked);
            });
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void SetMenuItem_ReplacesByCommandAndCanInsertAtAnIndex()
    {
        var (service, _, _) = CreateService(new FakeTrayIcon());

        try
        {
            // Replace an existing command in place.
            service.SetMenuItem(new TrayMenuItem(TrayMenuCommand.Exit, "Quit"));

            var items = service.GetMenuItems();
            Assert.Equal(3, items.Count);
            Assert.Equal(TrayMenuCommand.Exit, items[2].Command);
            Assert.Equal("Quit", items[2].Text);

            // Insert with a target position (the T-12 seam for the Edit Mode toggle).
            service.SetMenuItem(
                new TrayMenuItem(TrayMenuCommand.Exit, "Quit", IsChecked: true, IsCheckable: true),
                index: 0);

            items = service.GetMenuItems();
            Assert.Equal(3, items.Count);
            Assert.Equal(TrayMenuCommand.Exit, items[0].Command);
            Assert.Equal("Quit", items[0].Text);
            Assert.True(items[0].IsChecked);
            Assert.True(items[0].IsCheckable);
            Assert.Equal(TrayMenuCommand.ShowHidePanel, items[1].Command);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void HotkeysEditorItem_IsComposedAfterSettings()
    {
        // M3-RF-611: the composition root adds Edit Mode, Settings, and the focused hotkey editor
        // through the same SetMenuItem seam; Hotkeys lands after Settings and before the folder/exit
        // items, so the tray exposes the entry point without changing the M1 ordering.
        var (service, _, _) = CreateService(new FakeTrayIcon());

        try
        {
            service.SetMenuItem(new TrayMenuItem(TrayMenuCommand.EditMode, "Edit Mode", IsCheckable: true), index: 1);
            service.SetMenuItem(new TrayMenuItem(TrayMenuCommand.Settings, "Settings…"), index: 2);
            service.SetMenuItem(new TrayMenuItem(TrayMenuCommand.Hotkeys, "Hotkeys…"), index: 3);

            var items = service.GetMenuItems();

            Assert.Equal(6, items.Count);
            Assert.Equal(TrayMenuCommand.Settings, items[2].Command);
            Assert.Equal(TrayMenuCommand.Hotkeys, items[3].Command);
            Assert.Equal("Hotkeys…", items[3].Text);
            Assert.Equal(TrayMenuCommand.OpenConfigurationFolder, items[4].Command);
            Assert.Equal(TrayMenuCommand.Exit, items[5].Command);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void AboutItem_IsComposedAfterHotkeysAndBeforeTheFolderCommand()
    {
        // User-requested About window: the composition root adds it through the same SetMenuItem
        // seam used by Settings and Hotkeys, after Hotkeys and before the folder/exit items.
        var (service, _, _) = CreateService(new FakeTrayIcon());

        try
        {
            service.SetMenuItem(new TrayMenuItem(TrayMenuCommand.EditMode, "Edit Mode", IsCheckable: true), index: 1);
            service.SetMenuItem(new TrayMenuItem(TrayMenuCommand.Settings, "Settings…"), index: 2);
            service.SetMenuItem(new TrayMenuItem(TrayMenuCommand.Hotkeys, "Hotkeys…"), index: 3);
            service.SetMenuItem(new TrayMenuItem(TrayMenuCommand.About, "About TouchPanel…"), index: 4);

            var items = service.GetMenuItems();

            Assert.Equal(7, items.Count);
            Assert.Equal(TrayMenuCommand.Hotkeys, items[3].Command);
            Assert.Equal(TrayMenuCommand.About, items[4].Command);
            Assert.Equal("About TouchPanel…", items[4].Text);
            Assert.True(items[4].IsEnabled);
            Assert.False(items[4].IsCheckable);
            Assert.Equal(TrayMenuCommand.OpenConfigurationFolder, items[5].Command);
            Assert.Equal(TrayMenuCommand.Exit, items[6].Command);

            var selected = new List<TrayMenuCommand>();
            service.CommandSelected += (_, e) => selected.Add(e.Command);
            service.SelectCommand(TrayMenuCommand.About);

            Assert.Equal(new[] { TrayMenuCommand.About }, selected);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void EditModeToggle_IsAddedAsCheckableAfterShowHideAndItsStateCanBeUpdated()
    {
        // T-12/RF-005/RF-026: the composition root adds the Edit Mode item through the existing
        // SetMenuItem seam and flips its checked state as the mode changes.
        var (service, _, _) = CreateService(new FakeTrayIcon());

        try
        {
            service.SetMenuItem(
                new TrayMenuItem(TrayMenuCommand.EditMode, "Edit Mode", IsChecked: false, IsCheckable: true),
                index: 1);

            var items = service.GetMenuItems();
            Assert.Equal(4, items.Count);
            Assert.Equal(TrayMenuCommand.EditMode, items[1].Command);
            Assert.Equal("Edit Mode", items[1].Text);
            Assert.True(items[1].IsCheckable);
            Assert.False(items[1].IsChecked);

            service.SetMenuItem(
                new TrayMenuItem(TrayMenuCommand.EditMode, "Edit Mode", IsChecked: true, IsCheckable: true),
                index: 1);

            items = service.GetMenuItems();
            Assert.Equal(4, items.Count);
            Assert.True(items[1].IsChecked);

            var selected = new List<TrayMenuCommand>();
            service.CommandSelected += (_, e) => selected.Add(e.Command);
            service.SelectCommand(TrayMenuCommand.EditMode);

            Assert.Equal(new[] { TrayMenuCommand.EditMode }, selected);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void SelectCommand_RaisesCommandSelectedAndIsolatesHandlerFailures()
    {
        var (service, sink, _) = CreateService(new FakeTrayIcon());

        try
        {
            var received = new List<TrayMenuCommand>();
            service.CommandSelected += (_, e) => received.Add(e.Command);
            service.CommandSelected += (_, _) => throw new InvalidOperationException("bad handler");
            service.CommandSelected += (_, e) => received.Add(e.Command);

            service.SelectCommand(TrayMenuCommand.Exit);

            Assert.Equal(new[] { TrayMenuCommand.Exit, TrayMenuCommand.Exit }, received);
            Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void ShowBalloon_DelegatesToTheIconAndReturnsItsResult()
    {
        var icon = new FakeTrayIcon { BalloonResult = true };
        var (service, _, _) = CreateService(icon);

        try
        {
            var shown = service.ShowBalloon("Title", "Message", NotificationSeverity.Warning);

            Assert.True(shown);
            Assert.Equal(1, icon.BalloonCallCount);
            Assert.Equal("Title", icon.LastBalloonTitle);
            Assert.Equal("Message", icon.LastBalloonMessage);
            Assert.Equal(NotificationSeverity.Warning, icon.LastBalloonSeverity);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void ShowBalloon_WhenIconThrows_ReturnsFalseAndNeverThrows()
    {
        var icon = new FakeTrayIcon { ThrowOnBalloon = true };
        var (service, sink, _) = CreateService(icon);

        try
        {
            var shown = service.ShowBalloon("Title", "Message");

            Assert.False(shown);
            Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public void ShowBalloon_AfterDispose_ReturnsFalseWithoutTouchingTheIcon()
    {
        var icon = new FakeTrayIcon();
        var (service, _, _) = CreateService(icon);

        service.Dispose();

        Assert.False(service.ShowBalloon("Title", "Message"));
        Assert.Equal(0, icon.BalloonCallCount);
    }

    [Fact]
    public void Dispose_RemovesTheIconStopsRetriesAndIsIdempotent()
    {
        var icon = new FakeTrayIcon();
        icon.FailAdds(int.MaxValue);
        var (service, _, time) = CreateService(icon);

        service.Start();
        Assert.Equal(1, icon.AddCallCount);

        service.Dispose();
        service.Dispose();

        Assert.False(service.IsRegistered);
        Assert.Equal(1, icon.DeleteCallCount);
        Assert.Equal(1, icon.DisposeCallCount);

        // Pending retries are cancelled.
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, icon.AddCallCount);

        // The service is detached from the icon.
        icon.RaiseTaskbarCreated();
        Assert.Equal(0, icon.ReAddCallCount);
    }

    [Fact]
    public void Dispose_WhenIconOperationsThrow_DoesNotThrowAndLogs()
    {
        var icon = new FakeTrayIcon { ThrowOnDelete = true, ThrowOnDispose = true };
        var (service, sink, _) = CreateService(icon);
        service.Start();

        var exception = Record.Exception(service.Dispose);

        Assert.Null(exception);
        Assert.Contains(sink.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
    }

    [Fact]
    public void Start_AfterDispose_DoesNothing()
    {
        var icon = new FakeTrayIcon();
        var (service, _, _) = CreateService(icon);
        service.Dispose();

        service.Start();

        Assert.Equal(0, icon.AddCallCount);
        Assert.False(service.IsRegistered);
    }

    private static (TrayService Service, RecordingSink Sink, FakeTimeProvider Time) CreateService(
        FakeTrayIcon icon,
        TrayRegistrationRetryPolicy? retryPolicy = null)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();
        var time = new FakeTimeProvider();
        var options = new TrayServiceOptions
        {
            Tooltip = "TouchPanel",
            IconHandle = 42,
            RetryPolicy = retryPolicy ?? TrayRegistrationRetryPolicy.Default,
        };

        var service = new TrayService(icon, logger, options, time);
        return (service, sink, time);
    }

    private static object? ScalarProperty(LogEvent logEvent, string propertyName) =>
        logEvent.Properties.TryGetValue(propertyName, out var value) && value is ScalarValue scalar
            ? scalar.Value
            : null;

    /// <summary>In-memory <see cref="ITrayIcon"/> with scripted outcomes and event raising.</summary>
    private sealed class FakeTrayIcon : ITrayIcon
    {
        private int _addsToFail;
        private int _reAddsToFail;

        public event EventHandler<TrayIconMouseEventArgs>? MouseAction;

        public event EventHandler? TaskbarCreated;

        public bool IsRegistered { get; private set; }

        public int LastError { get; set; }

        public nint WindowHandle { get; set; }

        public bool BalloonResult { get; set; } = true;

        public bool ThrowOnAdd { get; set; }

        public bool ThrowOnReAdd { get; set; }

        public bool ThrowOnDelete { get; set; }

        public bool ThrowOnDispose { get; set; }

        public bool ThrowOnBalloon { get; set; }

        public int AddCallCount { get; private set; }

        public int ReAddCallCount { get; private set; }

        public int DeleteCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public int BalloonCallCount { get; private set; }

        public nint LastIconHandle { get; private set; }

        public string? LastTooltip { get; private set; }

        public string? LastBalloonTitle { get; private set; }

        public string? LastBalloonMessage { get; private set; }

        public NotificationSeverity LastBalloonSeverity { get; private set; }

        public void FailAdds(int count) => _addsToFail = count;

        public void FailReAdds(int count) => _reAddsToFail = count;

        public bool Add(nint iconHandle, string? tooltip = null)
        {
            AddCallCount++;
            if (ThrowOnAdd)
            {
                throw new InvalidOperationException("add failed");
            }

            LastIconHandle = iconHandle;
            LastTooltip = tooltip;

            if (_addsToFail > 0)
            {
                _addsToFail--;
                LastError = 87;
                IsRegistered = false;
                return false;
            }

            LastError = 0;
            IsRegistered = true;
            return true;
        }

        public bool ReAdd()
        {
            ReAddCallCount++;
            if (ThrowOnReAdd)
            {
                throw new InvalidOperationException("re-add failed");
            }

            if (_reAddsToFail > 0)
            {
                _reAddsToFail--;
                LastError = 87;
                IsRegistered = false;
                return false;
            }

            LastError = 0;
            IsRegistered = true;
            return true;
        }

        public bool Delete()
        {
            DeleteCallCount++;
            if (ThrowOnDelete)
            {
                throw new InvalidOperationException("delete failed");
            }

            IsRegistered = false;
            return true;
        }

        public bool ShowBalloon(string title, string message, NotificationSeverity severity)
        {
            BalloonCallCount++;
            if (ThrowOnBalloon)
            {
                throw new InvalidOperationException("balloon failed");
            }

            LastBalloonTitle = title;
            LastBalloonMessage = message;
            LastBalloonSeverity = severity;
            return BalloonResult;
        }

        public void Dispose()
        {
            DisposeCallCount++;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("dispose failed");
            }
        }

        public void RaiseMouseAction(int message, int x, int y) =>
            MouseAction?.Invoke(this, new TrayIconMouseEventArgs(message, x, y));

        public void RaiseTaskbarCreated()
        {
            IsRegistered = false;
            TaskbarCreated?.Invoke(this, EventArgs.Empty);
        }
    }
}
