using System.IO;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// A <see cref="FactAttribute"/> that is skipped with a clear reason when the Windows
/// "Segoe MDL2 Assets" font is unavailable (non-Windows hosts or CI images without the font), so the
/// glyph-coverage and rendered-icon checks never fail for an environment reason. xUnit 2.x has no
/// public dynamic-skip API, so the decision is made while the test is discovered.
/// </summary>
public sealed class Mdl2FontFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance, setting <see cref="FactAttribute.Skip"/> when the font is missing.</summary>
    public Mdl2FontFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Segoe MDL2 Assets is only available on Windows; glyph rendering is unverified.";
            return;
        }

        var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!File.Exists(Path.Combine(fontsDirectory, "segmdl2.ttf")))
        {
            Skip = "Segoe MDL2 Assets (segmdl2.ttf) is not installed; glyph rendering is unverified.";
        }
    }
}
