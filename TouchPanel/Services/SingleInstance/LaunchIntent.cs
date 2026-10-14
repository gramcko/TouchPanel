namespace TouchPanel.Services.SingleInstance;

/// <summary>
/// What a TouchPanel launch is asking for (RF-002/RF-006). The default is to start or signal the
/// panel; <see cref="Exit"/> is the explicit, user-approved recovery escape hatch.
/// </summary>
public enum LaunchIntent
{
    /// <summary>Start the panel, or ask the running instance to show it (historical behavior).</summary>
    ShowPanel,

    /// <summary>Ask the running instance to shut down; never start a panel.</summary>
    Exit,
}

/// <summary>
/// Pure parser for the startup command line (RF-006 "explicit shutdown command"). Only the
/// explicit <c>--exit</c> switch changes the default behavior; absent or unknown arguments keep
/// the historical "show panel" behavior, so the launcher is unaffected. Kept free of
/// <see cref="Environment"/> access so it is unit-testable.
/// </summary>
public static class LaunchIntentParser
{
    /// <summary>The command-line switch that asks the running instance to exit.</summary>
    public const string ExitArgument = "--exit";

    /// <summary>
    /// Classifies a command line (including the executable path as the first element, as returned
    /// by <see cref="Environment.GetCommandLineArgs"/>).
    /// </summary>
    /// <param name="arguments">The raw command-line arguments; <see langword="null"/> is treated as empty.</param>
    /// <returns><see cref="LaunchIntent.Exit"/> when <c>--exit</c> is present (case-insensitive); otherwise <see cref="LaunchIntent.ShowPanel"/>.</returns>
    public static LaunchIntent Parse(IReadOnlyList<string>? arguments)
    {
        if (arguments is not null)
        {
            for (var i = 0; i < arguments.Count; i++)
            {
                if (string.Equals(arguments[i], ExitArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return LaunchIntent.Exit;
                }
            }
        }

        return LaunchIntent.ShowPanel;
    }
}
