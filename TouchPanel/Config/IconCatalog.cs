namespace TouchPanel.Config;

/// <summary>
/// One selectable entry in the built-in icon catalog (RF-043/M2-RF-435): a stable id stored in
/// configuration, the glyph rendered by the widget templates (Segoe MDL2 Assets), and a
/// human-readable name for the edit-mode picker.
/// </summary>
/// <param name="Id">The stable catalog id persisted in <see cref="WidgetConfig.Icon"/>.</param>
/// <param name="Glyph">The font glyph rendered by the templates.</param>
/// <param name="DisplayName">The touch-friendly name shown in the icon picker.</param>
public sealed record IconOption(string Id, string Glyph, string DisplayName);

/// <summary>
/// The bundled, flat icon catalog (RF-043/M2-RF-435). It is an implementer-chosen fixed set of
/// built-in glyphs shipped with the application; external icon packs (RF-084) are out of scope.
/// Ids are stable persisted identifiers, so entries must be added, never renamed.
/// </summary>
public static class IconCatalog
{
    /// <summary>
    /// The built-in catalog. The count is deliberately small (well within the 1–64 bound of
    /// M2-RF-435) so the touch picker stays usable; every glyph resolves in the Windows
    /// "Segoe MDL2 Assets" font.
    /// </summary>
    public static IReadOnlyList<IconOption> BuiltIn { get; } =
    [
        new("power", "\uE7E8", "Power"),
        new("play", "\uE768", "Play"),
        new("pause", "\uE769", "Pause"),
        new("stop", "\uE71A", "Stop"),
        new("volume", "\uE767", "Volume"),
        new("mute", "\uE74F", "Mute"),
        new("settings", "\uE713", "Settings"),
        new("home", "\uE80F", "Home"),
        new("search", "\uE721", "Search"),
        new("star", "\uE734", "Star"),
        new("heart", "\uEB51", "Heart"),
        new("camera", "\uE722", "Camera"),
        new("mail", "\uE715", "Mail"),
        new("music", "\uE8D6", "Music"),
        new("video", "\uE714", "Video"),
        new("folder", "\uE8B7", "Folder"),
        new("document", "\uE8A5", "Document"),
        new("globe", "\uE774", "Globe"),
        new("cloud", "\uE753", "Cloud"),
        new("lock", "\uE72E", "Lock"),
        new("refresh", "\uE72C", "Refresh"),
        new("add", "\uE710", "Add"),
        new("remove", "\uE738", "Remove"),
        new("check", "\uE73E", "Check"),
        new("cancel", "\uE711", "Cancel"),
        new("edit", "\uE70F", "Edit"),
        new("delete", "\uE74D", "Delete"),
        new("save", "\uE74E", "Save"),
        new("share", "\uE72D", "Share"),
        new("clock", "\uE823", "Clock"),
        new("calendar", "\uE787", "Calendar"),
        new("bell", "\uE7E7", "Bell"),
        new("wifi", "\uE701", "Wi-Fi"),
        new("bluetooth", "\uE702", "Bluetooth"),
        new("battery", "\uE83F", "Battery"),
        new("cpu", "\uE950", "CPU"),
        new("monitor", "\uE7F4", "Monitor"),
        new("keyboard", "\uE765", "Keyboard"),
        new("mouse", "\uE962", "Mouse"),
        new("game", "\uE7FC", "Game"),
        new("map", "\uE707", "Map"),
        new("microphone", "\uE720", "Microphone"),
    ];

    /// <summary>Looks up a catalog entry by its persisted id.</summary>
    /// <param name="id">The catalog id, or <see langword="null"/> when no icon is configured.</param>
    /// <param name="option">The matching entry when found.</param>
    /// <returns><see langword="true"/> when the id is a built-in icon.</returns>
    public static bool TryGet(string? id, out IconOption option)
    {
        option = null!;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        foreach (var candidate in BuiltIn)
        {
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                option = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the glyph for a built-in icon id, or <see langword="null"/> when unset/unknown.</summary>
    /// <param name="id">The catalog id.</param>
    /// <returns>The glyph to render, or <see langword="null"/>.</returns>
    public static string? GlyphFor(string? id) => TryGet(id, out var option) ? option.Glyph : null;
}
