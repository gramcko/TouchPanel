using System.Net;
using System.Net.Http;
using System.Text;
using TouchPanel.Services.Updates;
using TouchPanel.Tests.TestSupport;

namespace TouchPanel.Tests.Services;

/// <summary>
/// Manual update-check service against a fake HTTP handler: success (newer/same/older), 404
/// no-releases, HTTP/transport errors, timeout, malformed and oversized payloads, and the HTTPS +
/// User-Agent invariants. No real network or UI is touched.
/// </summary>
public sealed class GitHubUpdateCheckServiceTests
{
    private static readonly VersionTag Current = new(1, 0, 2);

    [Fact]
    public async Task CheckForUpdate_NewerTag_ReportsUpdateAvailableWithReleaseLink()
    {
        const string payload =
            """
            {
              "tag_name": "v1.0.3",
              "html_url": "https://github.com/gramcko/TouchPanel/releases/tag/v1.0.3"
            }
            """;
        var (service, handler) = Create((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new VersionTag(1, 0, 3), result.LatestVersion);
        Assert.Equal("https://github.com/gramcko/TouchPanel/releases/tag/v1.0.3", result.ReleaseUrl);

        // HTTPS endpoint and GitHub's required User-Agent header.
        Assert.Equal(Uri.UriSchemeHttps, handler.LastRequestUri?.Scheme);
        Assert.Equal(GitHubUpdateCheckService.LatestReleaseUri, handler.LastRequestUri);
        Assert.Contains("TouchPanel/", handler.LastUserAgent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v1.0.2")]
    [InlineData("1.0.2")]
    [InlineData("v1.0.1")]
    [InlineData("v0.9.9")]
    public async Task CheckForUpdate_SameOrOlderTag_ReportsUpToDate(string tag)
    {
        var payload = $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/gramcko/TouchPanel/releases/tag/{{tag}}"}""";
        var (service, _) = Create((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.ReleaseUrl);
    }

    [Fact]
    public async Task CheckForUpdate_PreReleaseOfANewerCore_IsStillNewer()
    {
        const string payload = """{"tag_name":"v1.1.0-beta.1","html_url":"https://github.com/gramcko/TouchPanel/releases/tag/v1.1.0-beta.1"}""";
        var (service, _) = Create((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.1.0-beta.1", result.LatestVersion?.ToString());
    }

    [Fact]
    public async Task CheckForUpdate_NotFound_ReportsNoReleases()
    {
        var (service, _) = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(UpdateCheckStatus.NoReleases, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task CheckForUpdate_NonSuccess_ReportsFailure(HttpStatusCode status)
    {
        var (service, _) = Create((_, _) => Task.FromResult(new HttpResponseMessage(status)));

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckForUpdate_TransportError_ReportsFailureWithoutThrowing()
    {
        var (service, _) = Create((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")));

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckForUpdate_Timeout_ReportsFailurePromptly()
    {
        var (service, _) = Create(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage();
            },
            timeout: TimeSpan.FromMilliseconds(80));

        var started = DateTime.UtcNow;
        var result = await service.CheckForUpdateAsync();
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"The timeout took {elapsed}.");
    }

    [Fact]
    public async Task CheckForUpdate_PreCancelled_DoesNotSendARequest()
    {
        var (service, handler) = Create((_, _) => Task.FromResult(JsonResponse("""{"tag_name":"v9.9.9"}""")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.CheckForUpdateAsync(cancellation.Token);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{"html_url":"https://github.com/gramcko/TouchPanel/releases"}""")]
    [InlineData("""{"tag_name":123}""")]
    [InlineData("""{"tag_name":"latest-release"}""")]
    [InlineData("""{"tag_name":""}""")]
    public async Task CheckForUpdate_MalformedOrMissingTag_ReportsFailure(string payload)
    {
        var (service, _) = Create((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckForUpdate_NonHttpsReleaseLink_IsNotExposed()
    {
        const string payload = """{"tag_name":"v1.0.5","html_url":"http://example.test/release"}""";
        var (service, _) = Create((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.ReleaseUrl);
    }

    [Fact]
    public async Task CheckForUpdate_OversizedPayload_IsRejectedWithoutBufferingIt()
    {
        var payload = "{\"tag_name\":\"v9.9.9\",\"padding\":\"" + new string('x', GitHubUpdateCheckService.MaxResponseBytes) + "\"}";
        var (service, _) = Create((_, _) => Task.FromResult(JsonResponse(payload)));

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckForUpdate_NeverLogsTheResponseBody()
    {
        const string marker = "SUPER_SECRET_RESPONSE_BODY_MARKER";
        var payload = $$"""{"unexpected":"{{marker}}"}""";
        var (logger, sink) = TestLoggerFactory.Create();
        var (service, _) = Create((_, _) => Task.FromResult(JsonResponse(payload)), logger: logger);

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.DoesNotContain(
            sink.Events,
            logEvent => logEvent.RenderMessage().Contains(marker, StringComparison.Ordinal));
    }

    private static (GitHubUpdateCheckService Service, FakeHttpMessageHandler Handler) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        TimeSpan? timeout = null,
        Serilog.ILogger? logger = null)
    {
        var fake = new FakeHttpMessageHandler(handler);
        var client = new HttpClient(fake);
        return (new GitHubUpdateCheckService(Current, client, logger, timeout), fake);
    }

    private static HttpResponseMessage JsonResponse(string payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
}
