namespace TouchPanel.Config;

/// <summary>
/// One way to launch a preset application (user-requested preset app buttons): either a concrete
/// executable path (with optional fixed arguments) or an executable name looked up inside the newest
/// matching <see cref="DirectorySearchPattern"/> child directory. The catalog is WPF-free data; the
/// probing/expansion happens in <see cref="AppPresetDetector"/> with injectable seams so tests never
/// touch the real filesystem.
/// </summary>
/// <param name="ExecutablePath">
/// A full path template (environment variables are expanded at detection time) when
/// <paramref name="DirectoryRoot"/> is <see langword="null"/>; otherwise the executable file name to
/// look for inside each matching child directory.
/// </param>
/// <param name="Arguments">Fixed arguments passed to the executable, or <see langword="null"/> for none.</param>
/// <param name="DirectoryRoot">
/// The directory whose <paramref name="DirectorySearchPattern"/> children are searched newest-first,
/// or <see langword="null"/> for a direct path candidate.
/// </param>
/// <param name="DirectorySearchPattern">The child-directory glob (for example <c>app-*</c>); required with a root.</param>
public sealed record AppLaunchCandidate(
    string ExecutablePath,
    IReadOnlyList<string>? Arguments = null,
    string? DirectoryRoot = null,
    string? DirectorySearchPattern = null);

/// <summary>
/// One app offered by the edit-mode Apps row (user-requested preset buttons): its display name and
/// stable brand icon id, the ordered executable candidates, and an optional shell URI fallback for
/// apps that register one but are not found as an executable.
/// </summary>
/// <param name="Id">Stable preset id (diagnostics/tests; not persisted in the widget).</param>
/// <param name="DisplayName">The label and palette text, also persisted as the widget label.</param>
/// <param name="IconId">The brand icon id persisted as the widget <c>icon</c>.</param>
/// <param name="ExecutableCandidates">Ordered candidates; the first existing executable wins.</param>
/// <param name="FallbackUri">
/// A shell URI launched through Explorer when no executable is found, or <see langword="null"/> when
/// the app has no reliable URI (browsers, GitHub Desktop); those buttons are created with an empty
/// executable so the user can Browse to it.
/// </param>
public sealed record AppPreset(
    string Id,
    string DisplayName,
    string IconId,
    IReadOnlyList<AppLaunchCandidate> ExecutableCandidates,
    string? FallbackUri)
{
    /// <summary>Gets the brand glyph for the palette preview, or <see langword="null"/> for a vector icon.</summary>
    public string? IconGlyph => IconCatalog.GlyphFor(IconId);

    /// <summary>Gets the brand vector path for the palette preview.</summary>
    public string? IconPathData => IconCatalog.PathDataFor(IconId);
}

/// <summary>
/// The resolved launch information for one preset (user-requested preset app buttons). When
/// <see cref="UsedFallbackUri"/> is set, <see cref="ExecutablePath"/> is the Windows shell
/// (<c>explorer.exe</c>) and <see cref="Arguments"/> contains the single fallback URI.
/// </summary>
/// <param name="ExecutablePath">The detected executable, the shell for a URI fallback, or empty when nothing was found.</param>
/// <param name="Arguments">The fixed arguments (or the single fallback URI).</param>
/// <param name="FallbackUri">The URI used when no executable was found, or <see langword="null"/>.</param>
/// <param name="UsedFallbackUri">Whether the launch routes through the Windows shell URI.</param>
public sealed record AppPresetLaunch(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string? FallbackUri,
    bool UsedFallbackUri)
{
    /// <summary>Gets a value indicating whether a concrete executable was found.</summary>
    public bool HasExecutable => !string.IsNullOrWhiteSpace(ExecutablePath);
}

/// <summary>
/// WPF-free catalog of the ten preset applications offered by the edit-mode Apps row
/// (user-requested preset app buttons). Paths are stored as environment-variable templates and are
/// expanded only by <see cref="AppPresetDetector"/>; the catalog itself performs no I/O.
/// </summary>
public static class AppPresetCatalog
{
    private const string ProgramFilesX86 = @"%ProgramFiles(x86)%";
    private const string ProgramFiles = @"%ProgramFiles%";
    private const string LocalAppData = @"%LOCALAPPDATA%";
    private const string RoamingAppData = @"%APPDATA%";

    /// <summary>
    /// The exact preset list, in palette order: Steam, Discord, WhatsApp, Spotify, Chrome, Edge,
    /// Brave, Firefox, VS Code, GitHub Desktop. Icon ids are brand ids from <see cref="IconCatalog"/>.
    /// </summary>
    public static IReadOnlyList<AppPreset> All { get; } =
    [
        new(
            "steam",
            "Steam",
            "steam",
            [
                new($@"{ProgramFilesX86}\Steam\steam.exe"),
                new($@"{ProgramFiles}\Steam\steam.exe"),
            ],
            "steam://open/main"),
        new(
            "discord",
            "Discord",
            "discord",
            [
                new(
                    $@"{LocalAppData}\Discord\Update.exe",
                    ["--processStart", "Discord.exe"]),
                new(
                    "Discord.exe",
                    DirectoryRoot: $@"{LocalAppData}\Discord",
                    DirectorySearchPattern: "app-*"),
            ],
            "discord://"),
        new(
            "whatsapp",
            "WhatsApp",
            "whatsapp",
            [new($@"{LocalAppData}\Microsoft\WindowsApps\WhatsApp.exe")],
            "whatsapp://"),
        new(
            "spotify",
            "Spotify",
            "spotify",
            [
                new($@"{RoamingAppData}\Spotify\Spotify.exe"),
                new($@"{LocalAppData}\Microsoft\WindowsApps\Spotify.exe"),
            ],
            "spotify:"),
        new(
            "chrome",
            "Chrome",
            "chrome",
            [
                new($@"{ProgramFiles}\Google\Chrome\Application\chrome.exe"),
                new($@"{ProgramFilesX86}\Google\Chrome\Application\chrome.exe"),
                new($@"{LocalAppData}\Google\Chrome\Application\chrome.exe"),
            ],
            FallbackUri: null),
        new(
            "edge",
            "Edge",
            "edge",
            [
                new($@"{ProgramFilesX86}\Microsoft\Edge\Application\msedge.exe"),
                new($@"{ProgramFiles}\Microsoft\Edge\Application\msedge.exe"),
            ],
            FallbackUri: null),
        new(
            "brave",
            "Brave",
            "brave",
            [
                new($@"{ProgramFiles}\BraveSoftware\Brave-Browser\Application\brave.exe"),
                new($@"{LocalAppData}\BraveSoftware\Brave-Browser\Application\brave.exe"),
            ],
            FallbackUri: null),
        new(
            "firefox",
            "Firefox",
            "firefox",
            [
                new($@"{ProgramFiles}\Mozilla Firefox\firefox.exe"),
                new($@"{ProgramFilesX86}\Mozilla Firefox\firefox.exe"),
            ],
            FallbackUri: null),
        new(
            "vscode",
            "VS Code",
            "vscode",
            [
                new($@"{LocalAppData}\Programs\Microsoft VS Code\Code.exe"),
                new($@"{ProgramFiles}\Microsoft VS Code\Code.exe"),
            ],
            "vscode://"),
        new(
            "github-desktop",
            "GitHub Desktop",
            "github",
            [
                new($@"{LocalAppData}\GitHubDesktop\GitHubDesktop.exe"),
                new(
                    "GitHubDesktop.exe",
                    DirectoryRoot: $@"{LocalAppData}\GitHubDesktop",
                    DirectorySearchPattern: "app-*"),
            ],
            FallbackUri: null),
    ];
}

/// <summary>
/// Builds the <see cref="ImageButtonWidgetConfig"/> for a preset app button (user-requested preset
/// buttons). Pure and WPF-free: the edit-mode service owns id generation, placement, persistence,
/// eager widget creation, and undo, exactly like <c>AddWidget</c>.
/// </summary>
public static class AppPresetWidgetFactory
{
    /// <summary>The fixed preset button size in DIPs.</summary>
    public const double ButtonSize = 120;

    /// <summary>
    /// Creates the image-button configuration: the brand icon, the app name as both label text and
    /// accessibility tooltip source, the label shown, the 120x120 size, and a run-command action with
    /// the detected executable (or shell URI, or an empty executable the user can Browse to).
    /// </summary>
    /// <param name="preset">The preset app; must not be <see langword="null"/>.</param>
    /// <param name="launch">The resolved launch information; must not be <see langword="null"/>.</param>
    /// <returns>A new configuration with geometry and id left for the edit service.</returns>
    public static ImageButtonWidgetConfig Create(AppPreset preset, AppPresetLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(launch);

        return new ImageButtonWidgetConfig
        {
            Icon = preset.IconId,
            Label = preset.DisplayName,
            ShowLabel = true,
            Width = ButtonSize,
            Height = ButtonSize,
            Action = new RunCommandActionConfig
            {
                Path = launch.ExecutablePath,
                Arguments = [.. launch.Arguments],
                WorkingDirectory = null,
            },
        };
    }
}
