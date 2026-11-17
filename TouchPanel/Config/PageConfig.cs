namespace TouchPanel.Config;

/// <summary>
/// One page of the panel (RF-037). Exactly one page is active in M1; page switching arrives in M2.
/// </summary>
public sealed class PageConfig
{
    private string _id = string.Empty;
    private string? _name;
    private string? _background;
    private string? _backgroundImage;
    private List<WidgetConfig> _widgets = [];

    /// <summary>Gets or sets the page identifier referenced by <see cref="PanelConfig.ActivePageId"/>.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the optional touch-friendly page label shown by the in-panel pager and the pages
    /// editor. When absent or blank the id is shown instead; the property is additive under schema
    /// v4 (no schema bump) and every other page field is preserved.
    /// </summary>
    public string? Name
    {
        get => _name;
        set => _name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Gets or sets the optional page background color. The value is a <c>#RRGGBB</c> or
    /// <c>#AARRGGBB</c> string, or <see langword="null"/> for the theme background. The property is
    /// additive under schema v4 (no schema bump); an invalid value is cleared with a warning by
    /// validation so the page always stays usable.
    /// </summary>
    public string? Background
    {
        get => _background;
        set => _background = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Gets or sets the optional page background image as an absolute file path, or
    /// <see langword="null"/> for no image. Additive under schema v4 (no schema bump); validation
    /// clears a relative, missing, or undecodable path with a warning and never fails the load.
    /// </summary>
    public string? BackgroundImage
    {
        get => _backgroundImage;
        set => _backgroundImage = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Gets or sets the absolutely positioned widgets; never <see langword="null"/> (an explicit JSON null is normalized).</summary>
    public List<WidgetConfig> Widgets
    {
        get => _widgets;
        set => _widgets = value ?? [];
    }
}
