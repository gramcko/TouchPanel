using TouchPanel.Actions;
using TouchPanel.Config;

namespace TouchPanel.Tests.Actions;

/// <summary>
/// RF-049/RF-081 security invariants of the pure launch-request builder: the executable stays a
/// single file name even with spaces, hostile argument strings pass through as literal discrete
/// values, no shell wrapper is ever introduced, and an empty executable path fails fast.
/// </summary>
public class ProcessLaunchRequestFactoryTests
{
    [Fact]
    public void Create_ExplicitExecutableWithSpaces_IsKeptAsSingleFileName()
    {
        var config = new RunCommandActionConfig { Path = @"C:\Program Files\My App\app.exe" };

        var request = ProcessLaunchRequestFactory.Create(config);

        Assert.Equal(@"C:\Program Files\My App\app.exe", request.FileName);
        Assert.Empty(request.ArgumentList);
        Assert.Null(request.WorkingDirectory);
    }

    [Fact]
    public void Create_HostileArgumentStrings_ArePassedThroughLiterally()
    {
        string[] hostile =
        [
            "&& calc.exe",
            "; rm -rf /",
            "$(whoami)",
            "`whoami`",
            "| more",
            "> C:\\out.txt",
            "%PATH%",
            "a b c",
            "\"quoted\"",
            "--flag=value with spaces",
        ];
        var config = new RunCommandActionConfig { Path = "tool.exe", Arguments = [.. hostile] };

        var request = ProcessLaunchRequestFactory.Create(config);

        Assert.Equal(hostile, request.ArgumentList);
    }

    [Fact]
    public void Create_NeverIntroducesAShellWrapper()
    {
        var config = new RunCommandActionConfig
        {
            Path = "notepad.exe",
            Arguments = ["calc.exe"],
        };

        var request = ProcessLaunchRequestFactory.Create(config);

        Assert.Equal("notepad.exe", request.FileName);
        Assert.DoesNotContain("cmd.exe", request.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", request.FileName, StringComparison.OrdinalIgnoreCase);
        var argument = Assert.Single(request.ArgumentList);
        Assert.Equal("calc.exe", argument);
        Assert.DoesNotContain("/c", request.ArgumentList);
        Assert.DoesNotContain("-Command", request.ArgumentList);
    }

    [Fact]
    public void Create_CopiesArguments_NotAliasedToConfig()
    {
        var config = new RunCommandActionConfig { Path = "tool.exe", Arguments = ["one"] };

        var request = ProcessLaunchRequestFactory.Create(config);
        config.Arguments.Add("two");

        Assert.Equal("one", Assert.Single(request.ArgumentList));
    }

    [Fact]
    public void Create_PreservesWorkingDirectoryWithSpaces()
    {
        var config = new RunCommandActionConfig { Path = "tool.exe", WorkingDirectory = @"C:\work dir" };

        var request = ProcessLaunchRequestFactory.Create(config);

        Assert.Equal(@"C:\work dir", request.WorkingDirectory);
    }

    [Fact]
    public void Create_BlankWorkingDirectory_BecomesNull()
    {
        var config = new RunCommandActionConfig { Path = "tool.exe", WorkingDirectory = "   " };

        var request = ProcessLaunchRequestFactory.Create(config);

        Assert.Null(request.WorkingDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyPath_Throws(string path)
    {
        var config = new RunCommandActionConfig { Path = path };

        Assert.Throws<ArgumentException>(() => ProcessLaunchRequestFactory.Create(config));
    }

    [Fact]
    public void Create_NullConfig_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ProcessLaunchRequestFactory.Create(null!));
}
