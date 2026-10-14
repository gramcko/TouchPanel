using TouchPanel.Interop;
using TouchPanel.Services.Notifications;
using TouchPanel.Services.Tray;

namespace TouchPanel.Tests.Services;

/// <summary>
/// The only logic <see cref="TrayIconAdapter"/> adds over the native wrapper is the
/// severity-to-balloon-icon mapping (RF-010); the rest is a transparent pass-through covered by
/// the T-05 service tests and the native tray checks in T-14.
/// </summary>
public sealed class TrayIconAdapterTests
{
    [Theory]
    [InlineData(NotificationSeverity.Info, TrayIconBalloonIcon.Info)]
    [InlineData(NotificationSeverity.Warning, TrayIconBalloonIcon.Warning)]
    [InlineData(NotificationSeverity.Error, TrayIconBalloonIcon.Error)]
    public void ToBalloonIcon_MapsTheApplicationSeverity(NotificationSeverity severity, TrayIconBalloonIcon expected)
    {
        Assert.Equal(expected, TrayIconAdapter.ToBalloonIcon(severity));
    }

    [Fact]
    public void ToBalloonIcon_UnknownValue_FallsBackToInfo()
    {
        Assert.Equal(TrayIconBalloonIcon.Info, TrayIconAdapter.ToBalloonIcon((NotificationSeverity)99));
    }
}
