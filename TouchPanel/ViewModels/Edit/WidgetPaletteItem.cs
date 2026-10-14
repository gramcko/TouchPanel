namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// One addable widget type offered by the edit-mode palette (RF-043/M2-RF-430/M2-RF-431, T-13).
/// Only the built-in registered types appear here, so unknown widget types are never offered for
/// add (<c>M2-RF-430</c>); the catalog is exposed by the widget registry
/// (<see cref="IWidgetPaletteSource.BuiltInWidgets"/>).
/// </summary>
/// <param name="Type">The config <c>type</c> discriminator passed back to the add command.</param>
/// <param name="DisplayName">The touch-friendly label shown on the palette button.</param>
/// <param name="DefaultWidth">
/// The type's registered default width in DIPs, or <c>0</c> when the type has none. The add
/// operation takes the larger of this and the fixed 160 DIP default (<c>M2-RF-431</c>).
/// </param>
/// <param name="DefaultHeight">
/// The type's registered default height in DIPs, or <c>0</c> when the type has none. The add
/// operation takes the larger of this and the fixed 120 DIP default (<c>M2-RF-431</c>).
/// </param>
public sealed record WidgetPaletteItem(
    string Type,
    string DisplayName,
    double DefaultWidth,
    double DefaultHeight);
