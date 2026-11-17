using System.Net.Http;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Deterministic <see cref="HttpMessageHandler"/> for update-check tests. It records the request URI
/// and User-Agent so HTTPS/header invariants can be asserted without any real network access.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    /// <summary>Gets the number of requests sent through this handler.</summary>
    public int Calls { get; private set; }

    /// <summary>Gets the last request URI.</summary>
    public Uri? LastRequestUri { get; private set; }

    /// <summary>Gets the last User-Agent header value.</summary>
    public string? LastUserAgent { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastRequestUri = request.RequestUri;
        LastUserAgent = request.Headers.UserAgent.ToString();
        return _handler(request, cancellationToken);
    }
}
