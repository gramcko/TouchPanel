using TouchPanel.Config;

namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// Read-only catalog of the widget types the edit-mode palette may add (RF-043/M2-RF-430/
/// M2-RF-431, T-13). The production implementation is <c>Widgets.WidgetRegistry</c>, which owns
/// both the type registrations and the per-type default configuration; the edit-mode service
/// depends only on this boundary so the palette stays free of the concrete registrations while
/// still refusing to add a type the build does not know.
/// </summary>
public interface IWidgetPaletteSource
{
    /// <summary>
    /// Gets the addable built-in widget types, in registration order. Unknown types never appear
    /// here and are therefore not offered for add (<c>M2-RF-430</c>).
    /// </summary>
    IReadOnlyList<WidgetPaletteItem> BuiltInWidgets { get; }

    /// <summary>
    /// Creates a fresh default configuration for one addable built-in type (the default type
    /// properties of <c>M2-RF-431</c>). Id and geometry are assigned by the caller.
    /// </summary>
    /// <param name="type">The config <c>type</c> discriminator to instantiate.</param>
    /// <param name="config">The new default configuration when the type is addable; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the type is a registered built-in with a default configuration.</returns>
    bool TryCreateDefaultWidget(string type, out WidgetConfig? config);
}
