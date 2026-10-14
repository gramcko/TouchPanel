namespace TouchPanel.Tests;

/// <summary>
/// Baseline smoke test: proves the test project runs and references the application
/// assembly. Replaced by real test suites as later tasks land (plan section 6.1).
/// </summary>
public class SmokeTests
{
    [Fact]
    public void TestProject_ReferencesApplicationAssembly()
    {
        var assemblyName = typeof(App).Assembly.GetName().Name;

        Assert.Equal("TouchPanel", assemblyName);
    }
}
