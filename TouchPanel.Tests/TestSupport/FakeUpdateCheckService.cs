using TouchPanel.Services.Links;
using TouchPanel.Services.Updates;

namespace TouchPanel.Tests.TestSupport;

/// <summary>Configurable in-memory <see cref="IUpdateCheckService"/>; never touches the network.</summary>
internal sealed class FakeUpdateCheckService : IUpdateCheckService
{
    /// <summary>Gets or sets the handler producing the result (or throwing) for one check.</summary>
    public Func<CancellationToken, Task<UpdateCheckResult>> Handler { get; set; } =
        _ => Task.FromResult(UpdateCheckResult.UpToDate());

    /// <summary>Gets the number of checks requested.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Handler(cancellationToken);
    }
}

/// <summary>Recording <see cref="IUrlLauncher"/>; never starts a browser.</summary>
internal sealed class RecordingUrlLauncher : IUrlLauncher
{
    /// <summary>Gets the URLs the view model asked to open, in order.</summary>
    public List<string> Opened { get; } = new();

    /// <summary>Gets or sets whether launches report success.</summary>
    public bool Result { get; set; } = true;

    /// <inheritdoc />
    public Task<bool> OpenAsync(string url, CancellationToken cancellationToken = default)
    {
        Opened.Add(url);
        return Task.FromResult(Result);
    }
}
