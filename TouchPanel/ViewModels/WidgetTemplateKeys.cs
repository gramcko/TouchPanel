namespace TouchPanel.ViewModels;

/// <summary>
/// Resource keys of the per-type widget <c>DataTemplate</c>s (DD-6/RF-032). The registry maps a
/// config <c>type</c> to one of these keys and the view model exposes it so
/// <c>Widgets.WidgetTemplateSelector</c> can resolve the template without any per-instance logic
/// in the host. Adding a widget type is one registration plus one template with a new key.
/// </summary>
public static class WidgetTemplateKeys
{
    /// <summary>Button widget template (T-10).</summary>
    public const string Button = "Widget.Button";

    /// <summary>Toggle widget template (T-10).</summary>
    public const string Toggle = "Widget.Toggle";

    /// <summary>Label widget template (T-10).</summary>
    public const string Label = "Widget.Label";

    /// <summary>Gauge widget template (T-11).</summary>
    public const string Gauge = "Widget.Gauge";

    /// <summary>Slider widget template (T-06).</summary>
    public const string Slider = "Widget.Slider";

    /// <summary>Sparkline widget template (T-07).</summary>
    public const string Sparkline = "Widget.Sparkline";

    /// <summary>Clock widget template (RF-046/M3-RF-462).</summary>
    public const string Clock = "Widget.Clock";

    /// <summary>ImageButton widget template (RF-046/M3-RF-464).</summary>
    public const string ImageButton = "Widget.ImageButton";

    /// <summary>Bar widget template (RF-046/M3-RF-466).</summary>
    public const string Bar = "Widget.Bar";

    /// <summary>Image widget template (user-requested addition).</summary>
    public const string Image = "Widget.Image";

    /// <summary>
    /// Placeholder template for widgets whose type has no registration in this build (RF-047/
    /// RF-079): the widget is rendered disabled with an error indicator instead of being dropped.
    /// </summary>
    public const string Unsupported = "Widget.Unsupported";
}
