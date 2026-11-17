using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Serilog;

namespace TouchPanel.Services.Updates;

/// <summary>
/// Production <see cref="IUpdateCheckService"/>: queries the GitHub "latest release" endpoint over
/// HTTPS with a bounded response, a hard timeout, and a required <c>User-Agent</c>. No payload body
/// is ever logged. The check runs only when the user presses "Check for updates"; nothing in the
/// application calls it on startup or on a timer.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> may be injected for tests (with a fake handler); production uses a
/// shared client. The timeout is created per request through a linked token so a hung connection can
/// never block the UI thread. Any transport, status, or parse failure is reported as
/// <see cref="UpdateCheckStatus.Failed"/>, which the About view model renders as a neutral message.
/// </remarks>
public sealed class GitHubUpdateCheckService : IUpdateCheckService
{
    /// <summary>The GitHub endpoint for the newest published (non-draft, non-prerelease) release.</summary>
    public static readonly Uri LatestReleaseUri =
        new("https://api.github.com/repos/gramcko/TouchPanel/releases/latest", UriKind.Absolute);

    /// <summary>The default per-request timeout (10 seconds).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The maximum accepted response body (64 KiB); a larger payload is rejected.</summary>
    public const int MaxResponseBytes = 64 * 1024;

    private const string AcceptMediaType = "application/vnd.github+json";

    // GitHub rejects requests without a User-Agent. The header carries only product identity.
    private static readonly string UserAgentValue =
        $"{AppInfo.Name}/{AppInfo.Version} (+{AppInfo.RepositoryUrl})";

    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    {
        // The per-request linked token owns the deadline.
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly HttpClient _httpClient;
    private readonly VersionTag _currentVersion;
    private readonly ILogger _logger;
    private readonly TimeSpan _timeout;

    /// <summary>Initializes a new instance of the <see cref="GitHubUpdateCheckService"/> class.</summary>
    /// <param name="currentVersion">The running version used for comparison.</param>
    /// <param name="httpClient">HTTP client, normally supplied only by tests with a fake handler.</param>
    /// <param name="logger">Logger for summary-only diagnostics; defaults to the global Serilog logger.</param>
    /// <param name="timeout">Per-request timeout; defaults to <see cref="DefaultTimeout"/>.</param>
    public GitHubUpdateCheckService(
        VersionTag currentVersion,
        HttpClient? httpClient = null,
        ILogger? logger = null,
        TimeSpan? timeout = null)
    {
        _currentVersion = currentVersion;
        _httpClient = httpClient ?? SharedClient;
        _logger = (logger ?? Log.Logger).ForContext<GitHubUpdateCheckService>();
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        // Fail closed: the endpoint is a compile-time HTTPS constant, and a non-HTTPS URI is never
        // requested even if the constant is changed by mistake.
        if (LatestReleaseUri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.Warning("Update check: refusing a non-HTTPS release endpoint");
            return UpdateCheckResult.Failed();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return UpdateCheckResult.Failed();
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
            request.Headers.UserAgent.ParseAdd(UserAgentValue);
            request.Headers.Accept.ParseAdd(AcceptMediaType);

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // A repository without releases is a valid neutral state, not an error.
                _logger.Information("Update check: the repository has no published releases");
                return UpdateCheckResult.NoReleases();
            }

            if (!response.IsSuccessStatusCode)
            {
                // Status code only: never the body (RF/RNF: no payload logging).
                _logger.Warning(
                    "Update check: GitHub answered with status {StatusCode}",
                    (int)response.StatusCode);
                return UpdateCheckResult.Failed();
            }

            var payload = await ReadBoundedAsync(response.Content, timeoutSource.Token).ConfigureAwait(false);
            if (payload is null)
            {
                _logger.Warning(
                    "Update check: the release payload exceeded the {MaxBytes}-byte limit",
                    MaxResponseBytes);
                return UpdateCheckResult.Failed();
            }

            return Parse(payload);
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Update check: the request was cancelled or timed out");
            return UpdateCheckResult.Failed();
        }
        catch (HttpRequestException ex)
        {
            _logger.Warning("Update check: the request failed. Error={Error}", ex.GetType().Name);
            return UpdateCheckResult.Failed();
        }
        catch (Exception ex)
        {
            _logger.Warning("Update check: the check failed unexpectedly. Error={Error}", ex.GetType().Name);
            return UpdateCheckResult.Failed();
        }
    }

    private UpdateCheckResult Parse(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tag_name", out var tagElement) ||
                tagElement.ValueKind != JsonValueKind.String ||
                !VersionTag.TryParse(tagElement.GetString(), out var latest))
            {
                _logger.Warning("Update check: the release payload did not contain a valid tag_name");
                return UpdateCheckResult.Failed();
            }

            string? releaseUrl = null;
            if (root.TryGetProperty("html_url", out var urlElement) &&
                urlElement.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps)
            {
                releaseUrl = uri.AbsoluteUri;
            }

            return latest.CompareTo(_currentVersion) > 0
                ? UpdateCheckResult.UpdateAvailable(latest, releaseUrl)
                : UpdateCheckResult.UpToDate(latest);
        }
        catch (JsonException)
        {
            _logger.Warning("Update check: the release payload was not valid JSON");
            return UpdateCheckResult.Failed();
        }
    }

    /// <summary>
    /// Reads at most <see cref="MaxResponseBytes"/> from the response body. Returns
    /// <see langword="null"/> when the declared or actual size exceeds the bound, so a hostile or
    /// broken server cannot make the application buffer an unbounded payload.
    /// </summary>
    private static async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxResponseBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
