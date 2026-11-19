using TouchPanel.Interop;

namespace TouchPanel.Tests.Interop;

/// <summary>
/// Verifies the tray-icon extraction seam without touching the desktop: success returns the
/// extracted handle (used exactly once), while a zero handle or an exception falls back to the
/// shared stock icon and never throws. The real <c>ExtractIconEx</c> call is not exercised here.
/// </summary>
public sealed class ApplicationIconTests
{
    private const string ExecutablePath = @"C:\fake\TouchPanel.exe";

    [Fact]
    public void LoadTrayIcon_WhenExtractionSucceeds_ReturnsTheHandleAndUsesItOnce()
    {
        nint extracted = 0x1234;
        int calls = 0;
        string? observedPath = null;

        nint result = ApplicationIcon.LoadTrayIcon(ExecutablePath, path =>
        {
            calls++;
            observedPath = path;
            return extracted;
        });

        Assert.Equal(extracted, result);
        Assert.Equal(1, calls);
        Assert.Equal(ExecutablePath, observedPath);
    }

    [Fact]
    public void LoadTrayIcon_WhenExtractionReturnsZero_FallsBackToTheStockIcon()
    {
        nint stock = StockIcon.LoadApplicationIcon();
        int calls = 0;

        nint result = ApplicationIcon.LoadTrayIcon(ExecutablePath, _ =>
        {
            calls++;
            return nint.Zero;
        });

        Assert.Equal(1, calls);
        Assert.NotEqual(nint.Zero, result);
        Assert.Equal(stock, result);
    }

    [Fact]
    public void LoadTrayIcon_WhenExtractionThrows_FallsBackToTheStockIcon()
    {
        nint stock = StockIcon.LoadApplicationIcon();

        nint result = ApplicationIcon.LoadTrayIcon(
            ExecutablePath,
            _ => throw new InvalidOperationException("extraction failed"));

        Assert.NotEqual(nint.Zero, result);
        Assert.Equal(stock, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LoadTrayIcon_WithoutAProcessPath_FallsBackToTheStockIcon(string? path)
    {
        nint stock = StockIcon.LoadApplicationIcon();
        int calls = 0;

        nint result = ApplicationIcon.LoadTrayIcon(path, _ =>
        {
            calls++;
            return 0x5678;
        });

        Assert.Equal(0, calls);
        Assert.NotEqual(nint.Zero, result);
        Assert.Equal(stock, result);
    }
}
