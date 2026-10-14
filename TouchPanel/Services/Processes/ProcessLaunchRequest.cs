namespace TouchPanel.Services.Processes;

/// <summary>
/// A validated, ready-to-start process launch (plan section 1.3 boundary, DD-8). Produced only by
/// <c>ProcessLaunchRequestFactory</c>, which guarantees the executable is non-empty and each
/// argument is a discrete value (RF-049). Never contains a shell command line.
/// </summary>
/// <param name="FileName">The explicit executable path or name passed to <c>ProcessStartInfo.FileName</c>.</param>
/// <param name="ArgumentList">The discrete arguments, in order; passed to <c>ProcessStartInfo.ArgumentList</c> without parsing.</param>
/// <param name="WorkingDirectory">The optional working directory; <see langword="null"/> when unconfigured.</param>
public sealed record ProcessLaunchRequest(
    string FileName,
    IReadOnlyList<string> ArgumentList,
    string? WorkingDirectory = null);
