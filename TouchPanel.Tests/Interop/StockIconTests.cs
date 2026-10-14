using TouchPanel.Interop;

namespace TouchPanel.Tests.Interop;

/// <summary>
/// Verifies the stock-icon P/Invoke used for the M1 tray icon (RF-005) against the real
/// <c>user32</c> function. This loads a shared system icon only; it does not touch the tray.
/// </summary>
public sealed class StockIconTests
{
    [Fact]
    public void LoadApplicationIcon_ReturnsASharedSystemIconHandle()
    {
        Assert.NotEqual(nint.Zero, StockIcon.LoadApplicationIcon());
    }
}
