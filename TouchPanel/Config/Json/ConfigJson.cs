using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchPanel.Config.Json;

/// <summary>
/// Canonical <see cref="JsonSerializerOptions"/> for <c>panel.json</c> (DD-9): camelCase names,
/// indented output for hand editing, null properties omitted, and explicit converters registered
/// for the widget/action <c>type</c> discriminators plus the lenient enum reader.
/// </summary>
public static class ConfigJson
{
    /// <summary>Shared, thread-safe options instance. Do not mutate after first use.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>
    /// Creates a fresh options instance. Exposed so part 2 code (store/migrations/validation) and
    /// tests can derive variants without depending on the shared instance.
    /// </summary>
    /// <returns>A new configured options instance.</returns>
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            // The output is a user-editable JSON data file, not HTML/JavaScript, so relaxed
            // escaping keeps labels readable (for example accented characters) without changing
            // semantics.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.Converters.Add(new WidgetConfigJsonConverter());
        options.Converters.Add(new ActionConfigJsonConverter());
        options.Converters.Add(new LenientEnumJsonConverter<MonitorFallback>());
        options.Converters.Add(new LenientEnumJsonConverter<MediaKeyKind>());
        options.Converters.Add(new LenientEnumJsonConverter<VolumeMode>());
        options.Converters.Add(new LenientEnumJsonConverter<VolumeDirection>());
        options.Converters.Add(new LenientEnumJsonConverter<SliderOrientation>());
        options.Converters.Add(new LenientEnumJsonConverter<SparklineStyle>());
        options.Converters.Add(new LenientEnumJsonConverter<ClockMode>());
        options.Converters.Add(new LenientEnumJsonConverter<ImageStretchMode>());
        options.Converters.Add(new LenientEnumJsonConverter<DisplayUnit>());
        return options;
    }
}
