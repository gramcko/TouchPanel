using TouchPanel.Config;
using TouchPanel.ViewModels;

namespace TouchPanel.Widgets;

/// <summary>
/// One widget-type registration (DD-6/RF-033): the config <c>type</c> discriminator, the
/// <c>DataTemplate</c> resource key the host resolves, and the factory that projects a validated
/// config into its view model. Adding a widget type is one registration plus one template; the
/// optional palette metadata (display name, default config, default size) additionally makes the
/// type addable from the edit-mode palette (RF-043/M2-RF-431, T-13).
/// </summary>
public sealed class WidgetRegistration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetRegistration"/> class.
    /// </summary>
    /// <param name="type">The config <c>type</c> discriminator (for example <see cref="WidgetTypes.Button"/>).</param>
    /// <param name="templateKey">The template resource key (see <see cref="WidgetTemplateKeys"/>).</param>
    /// <param name="factory">Creates the type's view model from a validated config and the shared context.</param>
    /// <param name="displayName">The palette label; defaults to <paramref name="type"/> when omitted.</param>
    /// <param name="createDefaultConfig">
    /// Creates a fresh default configuration for the type so it can be added from the palette
    /// (M2-RF-431). <see langword="null"/> marks the type as not addable (for example a test-only
    /// or unknown registration).
    /// </param>
    /// <param name="defaultWidth">The type's registered default width in DIPs, or <c>0</c> when it has none.</param>
    /// <param name="defaultHeight">The type's registered default height in DIPs, or <c>0</c> when it has none.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="type"/> or <paramref name="templateKey"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public WidgetRegistration(
        string type,
        string templateKey,
        Func<WidgetConfig, WidgetCreationContext, WidgetViewModelBase> factory,
        string? displayName = null,
        Func<WidgetConfig>? createDefaultConfig = null,
        double defaultWidth = 0,
        double defaultHeight = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentNullException.ThrowIfNull(factory);

        Type = type;
        TemplateKey = templateKey;
        Factory = factory;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? type : displayName;
        CreateDefaultConfig = createDefaultConfig;
        DefaultWidth = defaultWidth;
        DefaultHeight = defaultHeight;
    }

    /// <summary>Gets the config <c>type</c> discriminator this registration handles.</summary>
    public string Type { get; }

    /// <summary>Gets the <c>DataTemplate</c> resource key the host selects for this type.</summary>
    public string TemplateKey { get; }

    /// <summary>Gets the factory that creates the type's view model.</summary>
    public Func<WidgetConfig, WidgetCreationContext, WidgetViewModelBase> Factory { get; }

    /// <summary>Gets the touch-friendly palette label for this type (RF-043/M2-RF-431).</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the factory that creates a fresh default configuration for the palette add operation
    /// (M2-RF-431), or <see langword="null"/> when this registration is not addable.
    /// </summary>
    public Func<WidgetConfig>? CreateDefaultConfig { get; }

    /// <summary>Gets the type's registered default width in DIPs, or <c>0</c> when it has none.</summary>
    public double DefaultWidth { get; }

    /// <summary>Gets the type's registered default height in DIPs, or <c>0</c> when it has none.</summary>
    public double DefaultHeight { get; }
}
