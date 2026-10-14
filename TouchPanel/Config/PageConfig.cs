namespace TouchPanel.Config;

/// <summary>
/// One page of the panel (RF-037). Exactly one page is active in M1; page switching arrives in M2.
/// </summary>
public sealed class PageConfig
{
    private string _id = string.Empty;
    private List<WidgetConfig> _widgets = [];

    /// <summary>Gets or sets the page identifier referenced by <see cref="PanelConfig.ActivePageId"/>.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Gets or sets the absolutely positioned widgets; never <see langword="null"/> (an explicit JSON null is normalized).</summary>
    public List<WidgetConfig> Widgets
    {
        get => _widgets;
        set => _widgets = value ?? [];
    }
}
