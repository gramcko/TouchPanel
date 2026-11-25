namespace TouchPanel.Config;

/// <summary>
/// The line-art vector group (user-requested catalog expansion): 24x24 stroked paths for
/// headphones/microphone variants. They are drawn with a uniform 2-unit pen (~8% of the icon box)
/// so the visual weight matches the MDL2 glyphs instead of the heavier filled brand paths. All three
/// ids are pack-independent, exactly like the brand vectors.
/// </summary>
public static partial class IconCatalog
{
    /// <summary>The shared line-art pen width in the 24-unit viewbox (~8.3% of the icon box).</summary>
    public const double LineArtStrokeUnits = 2d;

    private const string HeadphonesPath =
        "M4.5 15 V11 A7.5 7.5 0 0 1 19.5 11 V15 " +
        "M4.5 14.5 H3.25 A1.25 1.25 0 0 0 2 15.75 V19.75 A1.25 1.25 0 0 0 3.25 21 H4.5 Z " +
        "M19.5 14.5 H20.75 A1.25 1.25 0 0 1 22 15.75 V19.75 A1.25 1.25 0 0 1 20.75 21 H19.5 Z";

    private const string MicrophonePath =
        "M9 3 H15 A3 3 0 0 1 18 6 V11 A3 3 0 0 1 15 14 H9 A3 3 0 0 1 6 11 V6 A3 3 0 0 1 9 3 Z " +
        "M5.5 11 V11.5 A6.5 6.5 0 0 0 18.5 11.5 V11 " +
        "M12 18 V21 M8.5 21 H15.5";

    private static IReadOnlyList<IconOption> CreateLineArtIcons() =>
    [
        new(
            "headphones",
            string.Empty,
            "Headphones",
            HeadphonesPath,
            Stroke: true,
            StrokeUnits: LineArtStrokeUnits),
        new(
            "headphonesMuted",
            string.Empty,
            "Headphones muted",
            HeadphonesPath + " M4 20 L20 4",
            Stroke: true,
            StrokeUnits: LineArtStrokeUnits),
        new(
            "microphoneMuted",
            string.Empty,
            "Microphone muted",
            MicrophonePath + " M4 20 L20 4",
            Stroke: true,
            StrokeUnits: LineArtStrokeUnits),
    ];
}
