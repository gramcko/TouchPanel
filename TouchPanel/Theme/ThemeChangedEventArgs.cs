namespace TouchPanel.Theme;

/// <summary>
/// Raised by <see cref="ThemeManager"/> after a built-in theme has been applied successfully
/// (M3-RF-848). Consumers (the composition root and later appearance work) react to the event
/// rather than polling for theme changes.
/// </summary>
public sealed class ThemeChangedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="ThemeChangedEventArgs"/> class.</summary>
    /// <param name="themeId">The now-active canonical theme id.</param>
    /// <param name="previousThemeId">The previously active canonical theme id.</param>
    public ThemeChangedEventArgs(string themeId, string previousThemeId)
    {
        ThemeId = themeId;
        PreviousThemeId = previousThemeId;
    }

    /// <summary>Gets the now-active canonical theme id.</summary>
    public string ThemeId { get; }

    /// <summary>Gets the previously active canonical theme id.</summary>
    public string PreviousThemeId { get; }
}
