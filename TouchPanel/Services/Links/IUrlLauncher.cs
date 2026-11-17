namespace TouchPanel.Services.Links;

/// <summary>
/// Opens an external HTTP(S) link in the user's default browser. The seam exists so the About view
/// model can be tested without starting a browser; production uses <see cref="ShellUrlLauncher"/>.
/// </summary>
public interface IUrlLauncher
{
    /// <summary>Opens a link asynchronously.</summary>
    /// <param name="url">The absolute HTTP(S) URL.</param>
    /// <param name="cancellationToken">Cancels a launch that has not started yet.</param>
    /// <returns><see langword="true"/> when the shell accepted the link; never throws.</returns>
    Task<bool> OpenAsync(string url, CancellationToken cancellationToken = default);
}
