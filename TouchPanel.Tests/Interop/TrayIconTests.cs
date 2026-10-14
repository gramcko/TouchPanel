using TouchPanel.Interop;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Interop;

/// <summary>
/// Exercises the testable surface of <see cref="TrayIcon"/> without touching the real tray:
/// pure callback decoding and truncation, event plumbing through a real hidden top-level
/// window, and the state guarantees for an unregistered icon. Native tray rendering is verified
/// manually later (T-14/MV-07); no <c>Shell_NotifyIcon</c> call is made by these tests.
/// </summary>
public sealed class TrayIconTests
{
    [Fact]
    public void NativeStructSize_MatchesWindowsSdkLayout()
    {
        // Full NOTIFYICONDATAW (required for NIF_INFO and NOTIFYICON_VERSION_4).
        var expected = Environment.Is64BitProcess ? 976 : 956;

        Assert.Equal(expected, TrayIcon.NativeStructSize);
    }

    [Theory]
    [InlineData(TrayIcon.NinSelect, TrayIcon.WmLButtonUp)]
    [InlineData(TrayIcon.NinKeySelect, TrayIcon.WmLButtonUp)]
    [InlineData(TrayIcon.WmContextMenu, TrayIcon.WmRButtonUp)]
    public void DecodeMouseAction_Version4Layout_MapsNotificationToMenuTrigger(int notification, int expectedMessage)
    {
        // Real NOTIFYICON_VERSION_4 layout: wParam = MAKELPARAM(x, y); lParam = MAKELPARAM(event, iconId).
        nint wParam = MakeLParam(lo: 400, hi: 300);
        nint lParam = MakeLParam(lo: notification, hi: (int)TrayIcon.DefaultIconId);

        var args = TrayIcon.DecodeMouseAction(wParam, lParam, usesVersion4: true);

        Assert.NotNull(args);
        Assert.Equal(expectedMessage, args.Message);
        Assert.Equal(400, args.X);
        Assert.Equal(300, args.Y);
    }

    [Fact]
    public void DecodeMouseAction_LegacyLayout_DecodesMessageWithoutPosition()
    {
        var args = TrayIcon.DecodeMouseAction((nint)TrayIcon.DefaultIconId, (nint)TrayIcon.WmRButtonUp, usesVersion4: false);

        Assert.NotNull(args);
        Assert.Equal(TrayIcon.WmRButtonUp, args.Message);
        Assert.Equal(0, args.X);
        Assert.Equal(0, args.Y);
    }

    [Fact]
    public void DecodeMouseAction_LegacyLayout_DecodesEveryButtonMessage()
    {
        int[] messages =
        [
            TrayIcon.WmLButtonDown,
            TrayIcon.WmLButtonUp,
            TrayIcon.WmLButtonDblClk,
            TrayIcon.WmRButtonDown,
            TrayIcon.WmRButtonUp,
            TrayIcon.WmRButtonDblClk,
        ];

        foreach (var message in messages)
        {
            var args = TrayIcon.DecodeMouseAction((nint)TrayIcon.DefaultIconId, (nint)message, usesVersion4: false);
            Assert.NotNull(args);
            Assert.Equal(message, args.Message);
        }
    }

    [Fact]
    public void DecodeMouseAction_Version4NegativeCoordinates_AreSignExtended()
    {
        // lParam carries the event; wParam carries x = -10 (0xFFF6) and y = 300 (0x012C).
        nint wParam = unchecked((nint)0x012CFFF6);
        nint lParam = MakeLParam(lo: TrayIcon.NinSelect, hi: (int)TrayIcon.DefaultIconId);

        var args = TrayIcon.DecodeMouseAction(wParam, lParam, usesVersion4: true);

        Assert.NotNull(args);
        Assert.Equal(TrayIcon.WmLButtonUp, args.Message);
        Assert.Equal(-10, args.X);
        Assert.Equal(300, args.Y);
    }

    [Theory]
    [InlineData(0x0402)] // NIN_BALLOONSHOW
    [InlineData(0x0403)] // NIN_BALLOONHIDE
    [InlineData(0x0404)] // NIN_BALLOONTIMEOUT
    [InlineData(0x0405)] // NIN_BALLOONUSERCLICK
    [InlineData(0x0406)] // NIN_POPUPOPEN
    [InlineData(0x0407)] // NIN_POPUPCLOSE
    public void DecodeMouseAction_Version4BalloonAndPopupEvents_AreIgnored(int notification)
    {
        nint wParam = MakeLParam(lo: 10, hi: 20);
        nint lParam = MakeLParam(lo: notification, hi: (int)TrayIcon.DefaultIconId);

        Assert.Null(TrayIcon.DecodeMouseAction(wParam, lParam, usesVersion4: true));
    }

    [Fact]
    public void DecodeMouseAction_NonMenuMessage_ReturnsNull()
    {
        // WM_MOUSEMOVE is not a menu trigger in the legacy layout.
        Assert.Null(TrayIcon.DecodeMouseAction(1, 0x0200, usesVersion4: false));

        // An unrecognized notification is ignored in the version-4 layout.
        nint wParam = MakeLParam(lo: 5, hi: 6);
        nint lParam = MakeLParam(lo: 0x0200, hi: (int)TrayIcon.DefaultIconId);
        Assert.Null(TrayIcon.DecodeMouseAction(wParam, lParam, usesVersion4: true));
    }

    /// <summary>Builds a Win32 <c>MAKELPARAM</c> from two 16-bit halves.</summary>
    private static nint MakeLParam(int lo, int hi) => (nint)(uint)(((lo & 0xFFFF) | ((hi & 0xFFFF) << 16)));

    [Fact]
    public void Truncate_ShortValue_IsUnchanged()
    {
        Assert.Equal("tooltip", TrayIcon.Truncate("tooltip", 127));
    }

    [Fact]
    public void Truncate_LongValue_IsCutToLimit()
    {
        Assert.Equal("abc", TrayIcon.Truncate("abcdef", 3));
    }

    [Fact]
    public void Truncate_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TrayIcon.Truncate(null, 5));
        Assert.Equal(string.Empty, TrayIcon.Truncate(string.Empty, 5));
    }

    [Fact]
    public void Truncate_DoesNotSplitSurrogatePairs()
    {
        var value = "ab" + char.ConvertFromUtf32(0x1F600); // 'a', 'b', high, low

        Assert.Equal("ab", TrayIcon.Truncate(value, 3));
    }

    [Fact]
    public async Task Constructor_WithMessageOnlyWindow_IsRejected()
    {
        using var sta = new StaThread();
        var name = "TouchPanel.Tests.TrayIcon.MessageOnly." + Guid.NewGuid().ToString("N");
        var window = await sta.InvokeAsync(() => new MessageWindow(name));

        try
        {
            // Message-only windows do not receive broadcasts, so TaskbarCreated would silently
            // never arrive; the constructor must reject the configuration.
            var exception = await Record.ExceptionAsync(() =>
                sta.InvokeAsync(() => new TrayIcon(messageWindow: window)));

            Assert.IsType<ArgumentException>(exception);
        }
        finally
        {
            await sta.InvokeAsync(window.Dispose);
        }
    }

    [Fact]
    public async Task MouseAction_IsRaisedForPostedCallbackMessage()
    {
        using var sta = new StaThread();
        TrayIcon? icon = null;
        var received = new TaskCompletionSource<TrayIconMouseEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        await sta.InvokeAsync(() =>
        {
            icon = new TrayIcon();
            icon.MouseAction += (_, e) => received.TrySetResult(e);
        });

        try
        {
            Assert.NotEqual(nint.Zero, icon!.WindowHandle);

            // Without an Add call there is no negotiated version, so the legacy layout is in
            // force: wParam is the icon id and lParam is the mouse message.
            nint wParam = (nint)TrayIcon.DefaultIconId;
            nint lParam = (nint)TrayIcon.WmRButtonUp;
            Assert.True(MessageWindow.PostMessage(icon.WindowHandle, TrayIcon.CallbackMessage, wParam, lParam));

            var args = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(TrayIcon.WmRButtonUp, args.Message);
            Assert.Equal(0, args.X);
            Assert.Equal(0, args.Y);
        }
        finally
        {
            await sta.InvokeAsync(icon!.Dispose);
        }
    }

    [Fact]
    public async Task TaskbarCreated_IsForwardedToOwner()
    {
        using var sta = new StaThread();
        TrayIcon? icon = null;
        var raised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await sta.InvokeAsync(() =>
        {
            icon = new TrayIcon();
            icon.TaskbarCreated += (_, _) => raised.TrySetResult();
        });

        try
        {
            var taskbarCreated = MessageWindow.RegisterWindowMessage(MessageWindow.TaskbarCreatedMessageName);
            Assert.NotEqual(0u, taskbarCreated);
            Assert.True(MessageWindow.PostMessage(icon!.WindowHandle, taskbarCreated));

            await raised.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await sta.InvokeAsync(icon!.Dispose);
        }
    }

    [Fact]
    public async Task UnregisteredIcon_ModifyReAddAndShowBalloon_ReturnFalseWithoutShellCalls()
    {
        using var sta = new StaThread();
        var icon = await sta.InvokeAsync(() => new TrayIcon());

        try
        {
            Assert.False(icon.IsRegistered);
            Assert.False(await sta.InvokeAsync(() => icon.Modify(0, "tooltip")));
            Assert.False(await sta.InvokeAsync(icon.ReAdd));
            Assert.False(await sta.InvokeAsync(() => icon.ShowBalloon("title", "message")));
            Assert.True(await sta.InvokeAsync(icon.Delete));
        }
        finally
        {
            await sta.InvokeAsync(icon.Dispose);
        }
    }

    [Fact]
    public async Task Dispose_WithoutRegistration_DoesNotThrowAndIsIdempotent()
    {
        using var sta = new StaThread();
        var icon = await sta.InvokeAsync(() => new TrayIcon());

        var exception = await Record.ExceptionAsync(() => sta.InvokeAsync(() =>
        {
            icon.Dispose();
            icon.Dispose();
        }));

        Assert.Null(exception);
    }
}
