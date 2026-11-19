using System.IO;
using TouchPanel.Config;

namespace TouchPanel.Tests.Config;

/// <summary>
/// User-requested preset app buttons: catalog content (exact ten apps, icon ids, executable
/// candidates, URI fallbacks), injectable detection order, app-* newest-first search, nothing-found
/// behavior, and the exact image-button configuration produced for each outcome. Every test uses
/// fake probes/enumeration, so no real filesystem or user profile is touched.
/// </summary>
public sealed class AppPresetCatalogTests
{
    private static readonly string[] ExpectedNames =
    [
        "Steam",
        "Discord",
        "WhatsApp",
        "Spotify",
        "Chrome",
        "Edge",
        "Brave",
        "Firefox",
        "VS Code",
        "GitHub Desktop",
    ];

    [Fact]
    public void Catalog_ContainsTheExactTenAppsWithBrandIcons()
    {
        Assert.Equal(ExpectedNames, AppPresetCatalog.All.Select(preset => preset.DisplayName).ToArray());
        Assert.Equal(
            new[]
            {
                "steam",
                "discord",
                "whatsapp",
                "spotify",
                "chrome",
                "edge",
                "brave",
                "firefox",
                "vscode",
                "github",
            },
            AppPresetCatalog.All.Select(preset => preset.IconId).ToArray());

        Assert.All(AppPresetCatalog.All, preset =>
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.IconPathData));
            Assert.Null(preset.IconGlyph);
            Assert.NotEmpty(preset.ExecutableCandidates);
        });
    }

    [Fact]
    public void Catalog_DeclaresTheRequestedExecutablesAndUriFallbacks()
    {
        var byId = AppPresetCatalog.All.ToDictionary(preset => preset.Id);

        Assert.Contains(@"%ProgramFiles(x86)%\Steam\steam.exe", byId["steam"].ExecutableCandidates.Select(c => c.ExecutablePath));
        Assert.Equal("steam://open/main", byId["steam"].FallbackUri);
        Assert.Equal(
            new[] { "--processStart", "Discord.exe" },
            byId["discord"].ExecutableCandidates[0].Arguments);
        Assert.Equal("discord://", byId["discord"].FallbackUri);
        Assert.Equal("app-*", byId["discord"].ExecutableCandidates[1].DirectorySearchPattern);
        Assert.Equal("whatsapp://", byId["whatsapp"].FallbackUri);
        Assert.Equal("spotify:", byId["spotify"].FallbackUri);
        Assert.Null(byId["chrome"].FallbackUri);
        Assert.Null(byId["edge"].FallbackUri);
        Assert.Null(byId["brave"].FallbackUri);
        Assert.Null(byId["firefox"].FallbackUri);
        Assert.Equal("vscode://", byId["vscode"].FallbackUri);
        Assert.Null(byId["github-desktop"].FallbackUri);
        Assert.Equal("app-*", byId["github-desktop"].ExecutableCandidates[1].DirectorySearchPattern);
    }

    [Fact]
    public void Detect_PicksTheFirstExistingCandidateInOrder()
    {
        var steam = Preset("steam");
        var first = AppPresetDetector.ExpandPath(steam.ExecutableCandidates[0].ExecutablePath);
        var second = AppPresetDetector.ExpandPath(steam.ExecutableCandidates[1].ExecutablePath);
        var probe = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first, second };
        var detector = new AppPresetDetector(path => probe.Contains(path));

        var launch = detector.Resolve(steam);

        Assert.Equal(first, launch.ExecutablePath);
        Assert.Empty(launch.Arguments);
        Assert.False(launch.UsedFallbackUri);
        Assert.True(launch.HasExecutable);
    }

    [Fact]
    public void Detect_UsesTheSecondCandidateWhenOnlyItExistsAndKeepsItsArguments()
    {
        var discord = Preset("discord");
        var candidate = discord.ExecutableCandidates[0];
        var path = AppPresetDetector.ExpandPath(candidate.ExecutablePath);
        var detector = new AppPresetDetector(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));

        var launch = detector.Resolve(discord);

        Assert.Equal(path, launch.ExecutablePath);
        Assert.Equal(candidate.Arguments, launch.Arguments);
        Assert.False(launch.UsedFallbackUri);
    }

    [Fact]
    public void Detect_SearchesAppDirectoriesNewestFirst()
    {
        var discord = Preset("discord");
        var globbed = discord.ExecutableCandidates[1];
        var root = AppPresetDetector.ExpandPath(globbed.DirectoryRoot!);
        var oldDirectory = Path.Combine(root, "app-1.0.9");
        var newDirectory = Path.Combine(root, "app-1.0.100");
        var irrelevant = Path.Combine(root, "other-9.9.9");

        var enumerated = 0;
        var detector = new AppPresetDetector(
            path => string.Equals(path, Path.Combine(newDirectory, globbed.ExecutablePath), StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, Path.Combine(oldDirectory, globbed.ExecutablePath), StringComparison.OrdinalIgnoreCase),
            searchedRoot =>
            {
                enumerated++;
                Assert.Equal(root, searchedRoot, ignoreCase: true);
                return [oldDirectory, irrelevant, newDirectory];
            });

        var launch = detector.Resolve(discord);

        Assert.Equal(1, enumerated);
        Assert.Equal(Path.Combine(newDirectory, "Discord.exe"), launch.ExecutablePath);
        Assert.Empty(launch.Arguments);
    }

    [Fact]
    public void Detect_GitHubDesktopUsesTheNewestAppDirectory()
    {
        var github = Preset("github-desktop");
        var globbed = github.ExecutableCandidates[1];
        var root = AppPresetDetector.ExpandPath(globbed.DirectoryRoot!);
        var newer = Path.Combine(root, "app-3.4.12");
        var older = Path.Combine(root, "app-3.4.2");

        var detector = new AppPresetDetector(
            path => path.Contains(@"\app-", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(@"\GitHubDesktop.exe", StringComparison.OrdinalIgnoreCase),
            _ => [older, newer]);

        var launch = detector.Resolve(github);

        Assert.Equal(Path.Combine(newer, "GitHubDesktop.exe"), launch.ExecutablePath);
    }

    [Fact]
    public void Detect_FallsBackToTheShellUriWhenNoExecutableExists()
    {
        var whatsapp = Preset("whatsapp");
        var detector = new AppPresetDetector(_ => false, _ => []);

        var launch = detector.Resolve(whatsapp);

        Assert.True(launch.UsedFallbackUri);
        Assert.True(launch.HasExecutable);
        Assert.Equal(AppPresetDetector.ExpandPath(AppPresetDetector.ExplorerPathTemplate), launch.ExecutablePath);
        Assert.Equal(new[] { "whatsapp://" }, launch.Arguments);
        Assert.Equal("whatsapp://", launch.FallbackUri);
    }

    [Fact]
    public void Detect_WhenNothingExistsAndThereIsNoUri_LeavesAnEmptyExecutable()
    {
        var chrome = Preset("chrome");
        var detector = new AppPresetDetector(_ => false, _ => []);

        var launch = detector.Resolve(chrome);

        Assert.False(launch.UsedFallbackUri);
        Assert.False(launch.HasExecutable);
        Assert.Equal(string.Empty, launch.ExecutablePath);
        Assert.Empty(launch.Arguments);
        Assert.Null(launch.FallbackUri);
    }

    [Fact]
    public void Detect_HostileOrThrowingProbeNeverThrows()
    {
        var chrome = Preset("chrome");
        var detector = new AppPresetDetector(_ => throw new IOException("denied"), _ => throw new IOException("denied"));

        var launch = detector.Resolve(chrome);

        Assert.False(launch.HasExecutable);
    }

    [Fact]
    public void Detect_DoesNotTouchTheRealFilesystemForTests()
    {
        // Every detector in this class supplies both seams; this test documents that expectation by
        // resolving all apps with an always-false probe and an always-empty enumerator.
        var detector = new AppPresetDetector(_ => false, _ => []);

        foreach (var preset in AppPresetCatalog.All)
        {
            var launch = detector.Resolve(preset);
            Assert.Equal(preset.FallbackUri is not null, launch.UsedFallbackUri);
            if (preset.FallbackUri is null)
            {
                Assert.False(launch.HasExecutable);
            }
        }
    }

    [Fact]
    public void Factory_ProducesTheExpectedImageButtonConfiguration()
    {
        var preset = Preset("steam");
        var launch = new AppPresetLaunch(
            @"C:\Program Files (x86)\Steam\steam.exe",
            ["-silent"],
            FallbackUri: null,
            UsedFallbackUri: false);

        var config = AppPresetWidgetFactory.Create(preset, launch);

        Assert.Equal("steam", config.Icon);
        Assert.Equal("Steam", config.Label);
        Assert.True(config.ShowLabel);
        Assert.Equal(120, config.Width);
        Assert.Equal(120, config.Height);
        Assert.Equal(string.Empty, config.Id);
        Assert.Equal(0, config.X);
        Assert.Equal(0, config.Y);

        var action = Assert.IsType<RunCommandActionConfig>(config.Action);
        Assert.Equal(launch.ExecutablePath, action.Path);
        Assert.Equal(new[] { "-silent" }, action.Arguments);
        Assert.Null(action.WorkingDirectory);
    }

    [Fact]
    public void Factory_ProducesTheShellUriActionWhenTheAppUsesTheFallback()
    {
        var preset = Preset("discord");
        var detector = new AppPresetDetector(_ => false, _ => []);
        var launch = detector.Resolve(preset);

        var config = AppPresetWidgetFactory.Create(preset, launch);
        var action = Assert.IsType<RunCommandActionConfig>(config.Action);

        Assert.Equal("discord", preset.IconId);
        Assert.Equal(AppPresetDetector.ExpandPath(AppPresetDetector.ExplorerPathTemplate), action.Path);
        Assert.Equal(new[] { "discord://" }, action.Arguments);
        Assert.Null(action.WorkingDirectory);
    }

    [Fact]
    public void Factory_ProducesAnEmptyExecutableWhenNothingWasFound()
    {
        var preset = Preset("chrome");
        var detector = new AppPresetDetector(_ => false, _ => []);
        var launch = detector.Resolve(preset);

        var config = AppPresetWidgetFactory.Create(preset, launch);
        var action = Assert.IsType<RunCommandActionConfig>(config.Action);

        Assert.Equal(string.Empty, action.Path);
        Assert.Empty(action.Arguments);
        Assert.Null(action.WorkingDirectory);
        Assert.True(config.ShowLabel);
        Assert.Equal("Chrome", config.Label);
        Assert.Equal("chrome", config.Icon);
    }

    private static AppPreset Preset(string id) =>
        AppPresetCatalog.All.Single(preset => preset.Id == id);
}
