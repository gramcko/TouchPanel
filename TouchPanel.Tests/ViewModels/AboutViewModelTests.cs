using System.Reflection;
using TouchPanel.Services.Updates;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.About;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// About view model: the version string comes from the assembly informational version, and the
/// manual update check exposes the four user-facing states (checking, up to date, update available,
/// unavailable) without touching the network, the UI, or the browser.
/// </summary>
public sealed class AboutViewModelTests
{
    [Fact]
    public void VersionText_ComesFromTheAssemblyInformationalVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(informational));

        var expected = informational!.Split('+')[0];
        var viewModel = CreateViewModel(new FakeUpdateCheckService(), new RecordingUrlLauncher());

        Assert.Equal(expected, viewModel.Version);
        Assert.Equal($"Version {expected}", viewModel.VersionText);
        Assert.DoesNotContain('+', viewModel.Version);
        Assert.Equal(AppInfo.Name, viewModel.AppName);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Description));
    }

    [Fact]
    public void NewViewModel_StartsIdleWithTheNeutralPrompt()
    {
        var service = new FakeUpdateCheckService();
        var viewModel = CreateViewModel(service, new RecordingUrlLauncher());

        Assert.Equal(0, service.Calls);
        Assert.Equal(AboutUpdateState.Idle, viewModel.UpdateState);
        Assert.Equal(AboutViewModel.IdleMessage, viewModel.StatusMessage);
        Assert.True(viewModel.HasStatusMessage);
        Assert.False(viewModel.IsChecking);
        Assert.True(viewModel.CanCheckForUpdates);
        Assert.False(viewModel.IsUpdateAvailable);
        Assert.False(viewModel.OpenReleaseCommand.CanExecute(null));
    }

    [Fact]
    public async Task CheckForUpdates_WhileInFlight_ReportsTheCheckingState()
    {
        var completion = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeUpdateCheckService { Handler = _ => completion.Task };
        var viewModel = CreateViewModel(service, new RecordingUrlLauncher());

        var check = viewModel.CheckForUpdatesAsync();

        Assert.True(viewModel.IsChecking);
        Assert.False(viewModel.CanCheckForUpdates);
        Assert.Equal(AboutUpdateState.Checking, viewModel.UpdateState);
        Assert.Equal(AboutViewModel.CheckingMessage, viewModel.StatusMessage);

        completion.SetResult(UpdateCheckResult.UpToDate(new VersionTag(1, 0, 2)));
        await check;

        Assert.False(viewModel.IsChecking);
        Assert.True(viewModel.CanCheckForUpdates);
    }

    [Fact]
    public async Task CheckForUpdates_WhenUpToDate_ShowsTheUpToDateMessage()
    {
        var service = new FakeUpdateCheckService
        {
            Handler = _ => Task.FromResult(UpdateCheckResult.UpToDate(new VersionTag(1, 0, 2))),
        };
        var viewModel = CreateViewModel(service, new RecordingUrlLauncher());

        await viewModel.CheckForUpdatesAsync();

        Assert.Equal(AboutUpdateState.UpToDate, viewModel.UpdateState);
        Assert.Equal(AboutViewModel.UpToDateMessage, viewModel.StatusMessage);
        Assert.False(viewModel.IsUpdateAvailable);
        Assert.Null(viewModel.ReleaseUrl);
    }

    [Fact]
    public async Task CheckForUpdates_WhenNewerReleaseExists_ShowsVersionAndCanOpenIt()
    {
        const string releaseUrl = "https://github.com/gramcko/TouchPanel/releases/tag/v1.0.3";
        var service = new FakeUpdateCheckService
        {
            Handler = _ => Task.FromResult(
                UpdateCheckResult.UpdateAvailable(new VersionTag(1, 0, 3), releaseUrl)),
        };
        var launcher = new RecordingUrlLauncher();
        var viewModel = CreateViewModel(service, launcher);

        await viewModel.CheckForUpdatesAsync();

        Assert.Equal(AboutUpdateState.UpdateAvailable, viewModel.UpdateState);
        Assert.Equal("Update available: v1.0.3", viewModel.StatusMessage);
        Assert.True(viewModel.IsUpdateAvailable);
        Assert.Equal(releaseUrl, viewModel.ReleaseUrl);
        Assert.True(viewModel.OpenReleaseCommand.CanExecute(null));

        viewModel.OpenReleaseCommand.Execute(null);

        Assert.Equal(new[] { releaseUrl }, launcher.Opened);
    }

    [Fact]
    public async Task CheckForUpdates_WhenServiceReportsFailure_ShowsTheNeutralMessage()
    {
        var service = new FakeUpdateCheckService
        {
            Handler = _ => Task.FromResult(UpdateCheckResult.Failed()),
        };
        var viewModel = CreateViewModel(service, new RecordingUrlLauncher());

        await viewModel.CheckForUpdatesAsync();

        Assert.Equal(AboutUpdateState.Unavailable, viewModel.UpdateState);
        Assert.Equal(AboutViewModel.FailedMessage, viewModel.StatusMessage);
        Assert.False(viewModel.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdates_WhenServiceThrows_IsContainedAsTheNeutralMessage()
    {
        var service = new FakeUpdateCheckService
        {
            Handler = _ => throw new InvalidOperationException("boom"),
        };
        var viewModel = CreateViewModel(service, new RecordingUrlLauncher());

        await viewModel.CheckForUpdatesAsync();

        Assert.Equal(AboutUpdateState.Unavailable, viewModel.UpdateState);
        Assert.Equal(AboutViewModel.FailedMessage, viewModel.StatusMessage);
        Assert.False(viewModel.IsChecking);
    }

    [Fact]
    public async Task CheckForUpdates_WhenNoReleasesExist_ShowsTheNeutralNoReleasesMessage()
    {
        var service = new FakeUpdateCheckService
        {
            Handler = _ => Task.FromResult(UpdateCheckResult.NoReleases()),
        };
        var viewModel = CreateViewModel(service, new RecordingUrlLauncher());

        await viewModel.CheckForUpdatesAsync();

        Assert.Equal(AboutUpdateState.Unavailable, viewModel.UpdateState);
        Assert.Equal(AboutViewModel.NoReleasesMessage, viewModel.StatusMessage);
        Assert.False(viewModel.IsUpdateAvailable);
    }

    [Fact]
    public void RepositoryAndReleasesCommands_OpenTheConfiguredLinks()
    {
        var launcher = new RecordingUrlLauncher();
        var viewModel = CreateViewModel(new FakeUpdateCheckService(), launcher);

        viewModel.OpenRepositoryCommand.Execute(null);
        viewModel.OpenReleasesCommand.Execute(null);

        Assert.Equal(new[] { AppInfo.RepositoryUrl, AppInfo.ReleasesUrl }, launcher.Opened);
        Assert.Equal("https://github.com/gramcko/TouchPanel", AppInfo.RepositoryUrl);
        Assert.Equal("https://github.com/gramcko/TouchPanel/releases", AppInfo.ReleasesUrl);
    }

    [Fact]
    public async Task CheckForUpdates_WhenAlreadyChecking_DoesNotStartASecondRequest()
    {
        var completion = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeUpdateCheckService { Handler = _ => completion.Task };
        var viewModel = CreateViewModel(service, new RecordingUrlLauncher());

        var first = viewModel.CheckForUpdatesAsync();
        await viewModel.CheckForUpdatesAsync();

        Assert.Equal(1, service.Calls);

        completion.SetResult(UpdateCheckResult.UpToDate());
        await first;
    }

    private static AboutViewModel CreateViewModel(IUpdateCheckService service, RecordingUrlLauncher launcher) =>
        new(service, launcher);
}
