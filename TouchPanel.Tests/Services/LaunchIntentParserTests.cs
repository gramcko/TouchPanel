using TouchPanel.Services.SingleInstance;

namespace TouchPanel.Tests.Services;

/// <summary>
/// Pure command-line classification for the RF-006 <c>--exit</c> escape hatch. Only the explicit
/// switch changes the default show-panel behavior; absent, null, or unknown arguments must not.
/// </summary>
public sealed class LaunchIntentParserTests
{
    [Theory]
    [InlineData("--exit")]
    [InlineData("--EXIT")]
    [InlineData("--Exit")]
    public void Parse_WithExitSwitch_IsExit(string argument)
    {
        var intent = LaunchIntentParser.Parse([@"C:\Tools\TouchPanel.exe", argument]);

        Assert.Equal(LaunchIntent.Exit, intent);
    }

    [Fact]
    public void Parse_WithoutArguments_IsShowPanel()
    {
        Assert.Equal(LaunchIntent.ShowPanel, LaunchIntentParser.Parse([]));
    }

    [Fact]
    public void Parse_WithNullArguments_IsShowPanel()
    {
        Assert.Equal(LaunchIntent.ShowPanel, LaunchIntentParser.Parse(null));
    }

    [Fact]
    public void Parse_WithUnrelatedArguments_IsShowPanel()
    {
        var intent = LaunchIntentParser.Parse([@"C:\Tools\TouchPanel.exe", "--verbose", "edit"]);

        Assert.Equal(LaunchIntent.ShowPanel, intent);
    }

    [Fact]
    public void Parse_WithExitAmongOtherArguments_IsExit()
    {
        var intent = LaunchIntentParser.Parse([@"C:\Tools\TouchPanel.exe", "--other", "--exit", "edit"]);

        Assert.Equal(LaunchIntent.Exit, intent);
    }

    [Fact]
    public void Parse_WithArgumentContainingExitAsSubstring_IsShowPanel()
    {
        // Only the exact switch counts; a value such as "--exit-code" must not trigger shutdown.
        var intent = LaunchIntentParser.Parse([@"C:\Tools\TouchPanel.exe", "--exit-code"]);

        Assert.Equal(LaunchIntent.ShowPanel, intent);
    }
}
