using System.IO;
using TouchPanel.Config;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Per-test isolated directory with injected <see cref="ConfigPaths"/> so configuration tests
/// never touch the real user profile (RNF-006). Cleanup is best-effort and never fails a test.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    /// <summary>Creates the unique directory and resolves the configuration paths against it.</summary>
    public TempDirectory()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "TouchPanel.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        Config = new ConfigPaths(DirectoryPath);
    }

    /// <summary>Gets the absolute path of the isolated directory.</summary>
    public string DirectoryPath { get; }

    /// <summary>Gets the injected configuration paths rooted at <see cref="DirectoryPath"/>.</summary>
    public ConfigPaths Config { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup only; a locked file must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }
}
