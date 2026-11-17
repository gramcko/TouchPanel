using System.Diagnostics;

namespace TouchPanel.Services.Links;

/// <summary>
/// Production <see cref="IUrlLauncher"/>: hands an absolute HTTP(S) URL to the shell, which opens
/// the user's default browser. No shell command line, no tracking of the launched process, and no
/// waiting, so a slow browser never blocks the UI thread. Failures are returned, never thrown.
/// </summary>
public sealed class ShellUrlLauncher : IUrlLauncher
{
    /// <inheritdoc />
    public Task<bool> OpenAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!TryNormalize(url, out var uri))
        {
            return Task.FromResult(false);
        }

        return Task.Run(
            () =>
            {
                try
                {
                    using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
                    {
                        UseShellExecute = true,
                    });

                    return process is not null;
                }
                catch (Exception)
                {
                    return false;
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Accepts only absolute HTTP(S) URLs so a malformed or non-web value is never routed to the
    /// shell. Exposed to tests without launching anything.
    /// </summary>
    /// <param name="url">The candidate URL.</param>
    /// <param name="uri">The parsed URI when accepted.</param>
    /// <returns><see langword="true"/> when the URL is an absolute HTTP(S) URL.</returns>
    internal static bool TryNormalize(string? url, out Uri uri)
    {
        uri = null!;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var candidate))
        {
            return false;
        }

        if (candidate.Scheme != Uri.UriSchemeHttps && candidate.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        uri = candidate;
        return true;
    }
}
