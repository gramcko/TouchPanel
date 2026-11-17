using System.Reflection;

namespace TouchPanel.Services.Updates;

/// <summary>
/// Single source of truth for the product identity shown by the About window. The version is read
/// from the assembly informational version, which MSBuild derives from the explicit
/// <c>&lt;Version&gt;</c> property in <c>TouchPanel.csproj</c>; nothing else hard-codes a version.
/// </summary>
public static class AppInfo
{
    /// <summary>The product name.</summary>
    public const string Name = "TouchPanel";

    /// <summary>A one-line product description for the About window.</summary>
    public const string Description =
        "A borderless, touch-first control panel for a secondary touchscreen that never steals focus.";

    /// <summary>The public GitHub repository.</summary>
    public const string RepositoryUrl = "https://github.com/gramcko/TouchPanel";

    /// <summary>The public releases page.</summary>
    public const string ReleasesUrl = RepositoryUrl + "/releases";

    /// <summary>The application version without build metadata, for example <c>1.0.2</c>.</summary>
    public static string Version { get; } = ResolveVersion(typeof(AppInfo).Assembly);

    /// <summary>The parsed application version used for release comparisons.</summary>
    public static VersionTag CurrentVersion { get; } =
        VersionTag.ParseOrNull(Version) ?? new VersionTag(0, 0, 0);

    /// <summary>
    /// Reads the informational version from an assembly, falling back to the assembly version when
    /// the attribute is missing, and strips the <c>+build</c> metadata for display and comparison.
    /// Exposed to tests so the source of truth can be verified without a desktop.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The display version.</returns>
    internal static string ResolveVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            informational = assembly.GetName().Version?.ToString() ?? "0.0.0";
        }

        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
