using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Serilog;
using TouchPanel.Services.Links;
using TouchPanel.Services.Updates;

namespace TouchPanel.ViewModels.About;

/// <summary>The visible state of the manual update check.</summary>
public enum AboutUpdateState
{
    /// <summary>No check has run yet in this window.</summary>
    Idle,

    /// <summary>A check is in flight.</summary>
    Checking,

    /// <summary>The running version is current.</summary>
    UpToDate,

    /// <summary>A newer release exists.</summary>
    UpdateAvailable,

    /// <summary>The check could not complete, or there is nothing to compare against.</summary>
    Unavailable,
}

/// <summary>
/// Presentation state for the About window. It shows the product identity and version sourced from
/// <see cref="AppInfo"/>, launches the repository/release links through <see cref="IUrlLauncher"/>,
/// and runs the manual update check through <see cref="IUpdateCheckService"/>. The check is always
/// asynchronous and user-initiated: constructing or opening the window performs no network work.
/// </summary>
public sealed class AboutViewModel : INotifyPropertyChanged
{
    /// <summary>The neutral message shown before the first check.</summary>
    public const string IdleMessage = "Check for updates to see if a newer version is available.";

    /// <summary>The message shown while a check is in flight.</summary>
    public const string CheckingMessage = "Checking for updates…";

    /// <summary>The message shown when the running version is current.</summary>
    public const string UpToDateMessage = "You're up to date";

    /// <summary>The message shown when no release exists to compare against.</summary>
    public const string NoReleasesMessage = "No published releases to compare against.";

    /// <summary>The neutral message shown when the check could not complete.</summary>
    public const string FailedMessage = "Couldn't check for updates";

    private readonly IUpdateCheckService _updateCheckService;
    private readonly IUrlLauncher _urlLauncher;
    private readonly ILogger _logger;
    private readonly RelayCommand _openReleaseCommand;

    private bool _isChecking;
    private AboutUpdateState _updateState = AboutUpdateState.Idle;
    private string _statusMessage = IdleMessage;
    private string? _releaseUrl;

    /// <summary>Initializes a new instance of the <see cref="AboutViewModel"/> class.</summary>
    /// <param name="updateCheckService">The injectable update-check seam; must not be <see langword="null"/>.</param>
    /// <param name="urlLauncher">The injectable browser-launch seam; must not be <see langword="null"/>.</param>
    /// <param name="logger">Logger for summary-only messages; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public AboutViewModel(
        IUpdateCheckService updateCheckService,
        IUrlLauncher urlLauncher,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(updateCheckService);
        ArgumentNullException.ThrowIfNull(urlLauncher);

        _updateCheckService = updateCheckService;
        _urlLauncher = urlLauncher;
        _logger = (logger ?? Log.Logger).ForContext<AboutViewModel>();

        _openReleaseCommand = new RelayCommand(
            () => _ = OpenUrlAsync(_releaseUrl),
            () => IsUpdateAvailable && !string.IsNullOrEmpty(_releaseUrl));

        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        OpenRepositoryCommand = new RelayCommand(() => _ = OpenUrlAsync(AppInfo.RepositoryUrl));
        OpenReleasesCommand = new RelayCommand(() => _ = OpenUrlAsync(AppInfo.ReleasesUrl));
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the product name.</summary>
    public string AppName => AppInfo.Name;

    /// <summary>Gets the display version, for example <c>Version 1.0.2</c>.</summary>
    public string VersionText => $"Version {AppInfo.Version}";

    /// <summary>Gets the raw version without build metadata, for example <c>1.0.2</c>.</summary>
    public string Version => AppInfo.Version;

    /// <summary>Gets the one-line product description.</summary>
    public string Description => AppInfo.Description;

    /// <summary>Gets the repository URL used by the GitHub link.</summary>
    public string RepositoryUrl => AppInfo.RepositoryUrl;

    /// <summary>Gets the releases URL used by the Releases link.</summary>
    public string ReleasesUrl => AppInfo.ReleasesUrl;

    /// <summary>Gets a value indicating whether a check is currently in flight.</summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                OnPropertyChanged(nameof(CanCheckForUpdates));
            }
        }
    }

    /// <summary>Gets a value indicating whether the Check for updates button should be enabled.</summary>
    public bool CanCheckForUpdates => !IsChecking;

    /// <summary>Gets the current update-check state.</summary>
    public AboutUpdateState UpdateState
    {
        get => _updateState;
        private set
        {
            if (SetProperty(ref _updateState, value))
            {
                OnPropertyChanged(nameof(IsUpdateAvailable));
            }
        }
    }

    /// <summary>Gets the user-facing status line for the update check.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    /// <summary>Gets a value indicating whether a status line is present.</summary>
    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Gets the HTTPS release page when a newer release exists.</summary>
    public string? ReleaseUrl
    {
        get => _releaseUrl;
        private set
        {
            if (SetProperty(ref _releaseUrl, value))
            {
                OnPropertyChanged(nameof(IsUpdateAvailable));
                _openReleaseCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets a value indicating whether a newer release was found and has a link.</summary>
    public bool IsUpdateAvailable =>
        UpdateState == AboutUpdateState.UpdateAvailable && !string.IsNullOrEmpty(_releaseUrl);

    /// <summary>Gets the command that runs the manual update check.</summary>
    public ICommand CheckForUpdatesCommand { get; }

    /// <summary>Gets the command that opens the GitHub repository.</summary>
    public ICommand OpenRepositoryCommand { get; }

    /// <summary>Gets the command that opens the releases page.</summary>
    public ICommand OpenReleasesCommand { get; }

    /// <summary>Gets the command that opens the latest release page when an update is available.</summary>
    public ICommand OpenReleaseCommand => _openReleaseCommand;

    /// <summary>
    /// Runs one manual update check. Never throws and never blocks the UI thread: the service runs
    /// asynchronously and every outcome becomes a status line. Concurrent invocations are ignored.
    /// </summary>
    /// <returns>A task that completes when the status reflects the outcome.</returns>
    public async Task CheckForUpdatesAsync()
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        ReleaseUrl = null;
        UpdateState = AboutUpdateState.Checking;
        StatusMessage = CheckingMessage;

        try
        {
            var result = await _updateCheckService.CheckForUpdateAsync().ConfigureAwait(true);
            ApplyResult(result);
        }
        catch (Exception ex)
        {
            // The service reports ordinary failures as Failed; this guard keeps an unexpected
            // implementation bug from escaping into the dispatcher.
            _logger.Warning(ex, "About: checking for updates failed unexpectedly");
            SetOutcome(AboutUpdateState.Unavailable, FailedMessage, releaseUrl: null);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void ApplyResult(UpdateCheckResult? result)
    {
        if (result is null)
        {
            SetOutcome(AboutUpdateState.Unavailable, FailedMessage, releaseUrl: null);
            return;
        }

        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable when result.LatestVersion is { } latest:
                SetOutcome(
                    AboutUpdateState.UpdateAvailable,
                    $"Update available: v{latest}",
                    result.ReleaseUrl);
                _logger.Information("About: a newer release is available. LatestVersion={Version}", latest.ToString());
                break;

            case UpdateCheckStatus.UpToDate:
                SetOutcome(AboutUpdateState.UpToDate, UpToDateMessage, releaseUrl: null);
                break;

            case UpdateCheckStatus.NoReleases:
                SetOutcome(AboutUpdateState.Unavailable, NoReleasesMessage, releaseUrl: null);
                break;

            default:
                SetOutcome(AboutUpdateState.Unavailable, FailedMessage, releaseUrl: null);
                break;
        }
    }

    private void SetOutcome(AboutUpdateState state, string message, string? releaseUrl)
    {
        // ReleaseUrl first so IsUpdateAvailable (raised by the state change) sees the final value.
        ReleaseUrl = releaseUrl;
        UpdateState = state;
        StatusMessage = message;
    }

    private async Task OpenUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var opened = await _urlLauncher.OpenAsync(url).ConfigureAwait(true);
            if (!opened)
            {
                _logger.Warning("About: the link could not be opened");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "About: opening a link failed");
        }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
