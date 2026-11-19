using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TouchPanel.Services.Updates;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.About;
using TouchPanel.Views.About;

namespace TouchPanel.Tests.Views;

/// <summary>
/// About-window wiring: it is an ordinary activating window (deliberate tray action), it surfaces
/// the product name, the assembly informational version, the one-line description, both links, and
/// the manual update-check affordances at the 40 DIP touch minimum. The XAML is instantiated on an
/// STA thread but never shown, so no desktop state or network call is touched.
/// </summary>
public sealed class AboutWindowTests
{
    [Fact]
    public async Task AboutWindow_MaterializesIdentityLinksAndUpdateControls()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var viewModel = new AboutViewModel(new FakeUpdateCheckService(), new RecordingUrlLauncher());
            var window = new AboutWindow(viewModel);
            try
            {
                WindowIconAssert.IsPackagedAppIcon(window);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                var appName = Assert.IsType<TextBlock>(window.FindName("AppNameText"));
                var version = Assert.IsType<TextBlock>(window.FindName("VersionTextBlock"));
                var description = Assert.IsType<TextBlock>(window.FindName("DescriptionText"));
                var repository = Assert.IsType<Button>(window.FindName("RepositoryButton"));
                var releases = Assert.IsType<Button>(window.FindName("ReleasesButton"));
                var check = Assert.IsType<Button>(window.FindName("CheckForUpdatesButton"));
                var openRelease = Assert.IsType<Button>(window.FindName("OpenReleaseButton"));
                var close = Assert.IsType<Button>(window.FindName("CloseButton"));
                var status = Assert.IsType<TextBlock>(window.FindName("UpdateStatusText"));

                return new
                {
                    ShowActivated = window.ShowActivated,
                    HasViewModel = ReferenceEquals(window.DataContext, viewModel),
                    CheckCommandBound = ReferenceEquals(check.Command, viewModel.CheckForUpdatesCommand),
                    Title = window.Title,
                    AppName = appName.Text,
                    Version = version.Text,
                    Description = description.Text,
                    RepositoryMinHeight = repository.MinHeight,
                    ReleasesMinHeight = releases.MinHeight,
                    CheckMinHeight = check.MinHeight,
                    OpenReleaseMinHeight = openRelease.MinHeight,
                    CloseMinHeight = close.MinHeight,
                    OpenReleaseVisibility = openRelease.Visibility,
                    Status = status.Text,
                };
            }
            finally
            {
                window.Close();
            }
        });

        // A deliberate activating window, bound to the About view model.
        Assert.True(result.ShowActivated);
        Assert.True(result.HasViewModel);
        Assert.True(result.CheckCommandBound);
        Assert.Equal("About TouchPanel", result.Title);

        Assert.Equal(AppInfo.Name, result.AppName);
        Assert.Contains(AppInfo.Version, result.Version, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));

        // RF-025/D-9 spirit: every affordance meets the 40 DIP touch minimum.
        Assert.True(result.RepositoryMinHeight >= 40);
        Assert.True(result.ReleasesMinHeight >= 40);
        Assert.True(result.CheckMinHeight >= 40);
        Assert.True(result.OpenReleaseMinHeight >= 40);
        Assert.True(result.CloseMinHeight >= 40);

        // No update has been checked, so the release link is hidden and the neutral prompt is shown.
        Assert.Equal(Visibility.Collapsed, result.OpenReleaseVisibility);
        Assert.Equal(AboutViewModel.IdleMessage, result.Status);
    }
}
