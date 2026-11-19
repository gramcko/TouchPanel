using System.IO;

namespace TouchPanel.Config;

/// <summary>
/// Detects how a preset app should be launched (user-requested preset app buttons). It expands the
/// catalog's environment-variable path templates, probes the candidates in order, searches
/// <c>app-*</c> install directories newest-first, and falls back to the app's shell URI when no
/// executable exists. File probing and directory enumeration are injectable so unit tests use fakes
/// and never touch the real filesystem.
/// </summary>
public sealed class AppPresetDetector
{
    /// <summary>The Windows shell used to hand a URI fallback to the registered protocol handler.</summary>
    public const string ExplorerPathTemplate = @"%SystemRoot%\explorer.exe";

    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, IReadOnlyList<string>> _enumerateDirectories;

    /// <summary>
    /// Initializes a new detector.
    /// </summary>
    /// <param name="fileExists">
    /// Injectable file probe (absolute path → exists). Defaults to <see cref="File.Exists"/>.
    /// </param>
    /// <param name="enumerateDirectories">
    /// Injectable child-directory enumerator (root → directories). Defaults to a safe
    /// <see cref="Directory.GetDirectories(string)"/> call that returns an empty list on any failure.
    /// </param>
    public AppPresetDetector(
        Func<string, bool>? fileExists = null,
        Func<string, IReadOnlyList<string>>? enumerateDirectories = null)
    {
        _fileExists = fileExists ?? File.Exists;
        _enumerateDirectories = enumerateDirectories ?? EnumerateDirectories;
    }

    /// <summary>
    /// Resolves the launch information for one preset: the first existing executable candidate wins;
    /// when none exists and the preset has a fallback URI, the Windows shell is used with the URI as
    /// its single argument; otherwise the executable is empty (the button is still creatable and the
    /// user can Browse for it). Never throws for a missing app.
    /// </summary>
    /// <param name="preset">The preset to resolve; must not be <see langword="null"/>.</param>
    /// <returns>The resolved launch information.</returns>
    public AppPresetLaunch Resolve(AppPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        foreach (var candidate in preset.ExecutableCandidates)
        {
            var executable = FindCandidate(candidate);
            if (executable is not null)
            {
                return new AppPresetLaunch(
                    executable,
                    candidate.Arguments ?? [],
                    FallbackUri: null,
                    UsedFallbackUri: false);
            }
        }

        if (!string.IsNullOrWhiteSpace(preset.FallbackUri))
        {
            return new AppPresetLaunch(
                ExpandPath(ExplorerPathTemplate),
                [preset.FallbackUri],
                preset.FallbackUri,
                UsedFallbackUri: true);
        }

        return new AppPresetLaunch(
            string.Empty,
            [],
            FallbackUri: null,
            UsedFallbackUri: false);
    }

    /// <summary>
    /// Expands the environment-variable templates in <paramref name="path"/> (for example
    /// <c>%ProgramFiles%</c>) without ever throwing for a hostile/unknown variable.
    /// </summary>
    /// <param name="path">The path or path template.</param>
    /// <returns>The expanded path (unknown variables are left as-is by the runtime).</returns>
    public static string ExpandPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        try
        {
            return Environment.ExpandEnvironmentVariables(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private string? FindCandidate(AppLaunchCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.ExecutablePath))
        {
            return null;
        }

        if (candidate.DirectoryRoot is null)
        {
            var direct = ExpandPath(candidate.ExecutablePath);
            return Exists(direct) ? direct : null;
        }

        if (string.IsNullOrWhiteSpace(candidate.DirectorySearchPattern))
        {
            return null;
        }

        var root = ExpandPath(candidate.DirectoryRoot);
        var directories = _enumerateDirectories(root);
        foreach (var directory in NewestFirst(directories, candidate.DirectorySearchPattern))
        {
            var executable = Path.Combine(directory, candidate.ExecutablePath);
            if (Exists(executable))
            {
                return executable;
            }
        }

        return null;
    }

    private bool Exists(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && _fileExists(path);
        }
        catch (Exception)
        {
            // A probe seam must never make detection fail; a hostile/failing probe means "not here".
            return false;
        }
    }

    /// <summary>
    /// Orders the matching child directories newest-first. The installer folders are version-named
    /// (<c>app-1.0.9001</c>), so the parsed version is the primary key and the name breaks ties
    /// deterministically. Directories that do not match the catalog pattern are ignored.
    /// </summary>
    private static IEnumerable<string> NewestFirst(
        IReadOnlyList<string> directories,
        string searchPattern)
    {
        var prefix = searchPattern.EndsWith('*') ? searchPattern[..^1] : searchPattern;

        return directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Where(directory => Path.GetFileName(directory)
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(directory => ParseVersion(directory, prefix))
            .ThenByDescending(directory => directory, StringComparer.OrdinalIgnoreCase);
    }

    private static Version ParseVersion(string directory, string prefix)
    {
        var name = Path.GetFileName(directory);
        var suffix = name.Length > prefix.Length ? name[prefix.Length..] : string.Empty;
        return Version.TryParse(suffix, out var version) ? version : new Version(0, 0);
    }

    private static IReadOnlyList<string> EnumerateDirectories(string root)
    {
        try
        {
            return Directory.Exists(root) ? Directory.GetDirectories(root) : [];
        }
        catch (Exception)
        {
            // Missing/denied install folders simply yield no candidates.
            return [];
        }
    }
}
