namespace TouchPanel.Config;

/// <summary>
/// The action/object glyph group (user-requested catalog expansion): verified "Segoe MDL2 Assets"
/// glyphs for common actions and devices, appended after the original catalog and brand icons so
/// every existing stable id keeps its position. Ids are additive, camelCase, and descriptive.
/// </summary>
public static partial class IconCatalog
{
    private static IReadOnlyList<IconOption> CreateActionIcons() =>
    [
        // Audio.
        new("headset", "\uE95B", "Headset"),
        new("volumeLow", "\uE993", "Volume low"),
        new("volumeMedium", "\uE994", "Volume medium"),
        new("volumeHigh", "\uE995", "Volume high"),
        new("microphoneOff", "\uEC54", "Microphone off"),

        // Display, power, and devices.
        new("brightness", "\uE706", "Brightness"),
        new("fullscreen", "\uE740", "Fullscreen"),
        new("restoreWindow", "\uE923", "Restore window"),
        new("sleep", "\uE708", "Sleep"),
        new("restart", "\uF83E", "Restart"),
        new("unlock", "\uE785", "Unlock"),
        new("key", "\uE8D7", "Key"),
        new("shield", "\uEA18", "Shield"),
        new("ethernet", "\uE839", "Ethernet"),
        new("usb", "\uE88E", "USB"),
        new("hardDrive", "\uEDA2", "Hard drive"),
        new("print", "\uE749", "Print"),
        new("screenshot", "\uF7ED", "Screenshot"),

        // Editing.
        new("undo", "\uE7A7", "Undo"),
        new("redo", "\uE7A6", "Redo"),
        new("copy", "\uE8C8", "Copy"),
        new("paste", "\uE77F", "Paste"),
        new("cut", "\uE8C6", "Cut"),

        // View controls.
        new("zoomIn", "\uE8A3", "Zoom in"),
        new("zoomOut", "\uE71F", "Zoom out"),
        new("filter", "\uE71C", "Filter"),
        new("sort", "\uE8CB", "Sort"),

        // Apps and people.
        new("calculator", "\uE8EF", "Calculator"),
        new("code", "\uE943", "Code"),
        new("chat", "\uE8F2", "Chat"),
        new("people", "\uE716", "People"),
        new("phone", "\uE717", "Phone"),
        new("videoCall", "\uE8AA", "Video call"),
        new("photos", "\uE91B", "Photos"),
        new("cart", "\uE7BF", "Cart"),
    ];
}
