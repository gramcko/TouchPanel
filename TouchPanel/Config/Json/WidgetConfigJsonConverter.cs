using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchPanel.Config.Json;

/// <summary>
/// DD-9 explicit widget polymorphism: reads the <c>type</c> discriminator and deserializes the
/// matching concrete <see cref="WidgetConfig"/> type. Unrecognized discriminator values are not an
/// error; they become <see cref="UnknownWidgetConfig"/> entries that validation can skip (RF-079)
/// while preserving the original JSON.
/// </summary>
public sealed class WidgetConfigJsonConverter : JsonConverter<WidgetConfig>
{
    /// <inheritdoc/>
    public override WidgetConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A widget entry must be a JSON object.");
        }

        if (!element.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("A widget entry requires a string 'type' discriminator.");
        }

        var type = typeElement.GetString()!;
        return type switch
        {
            WidgetTypes.Button => element.Deserialize<ButtonWidgetConfig>(options)!,
            WidgetTypes.Toggle => element.Deserialize<ToggleWidgetConfig>(options)!,
            WidgetTypes.Label => element.Deserialize<LabelWidgetConfig>(options)!,
            WidgetTypes.Gauge => element.Deserialize<GaugeWidgetConfig>(options)!,
            WidgetTypes.Slider => element.Deserialize<SliderWidgetConfig>(options)!,
            WidgetTypes.Sparkline => element.Deserialize<SparklineWidgetConfig>(options)!,
            WidgetTypes.Clock => element.Deserialize<ClockWidgetConfig>(options)!,
            WidgetTypes.ImageButton => element.Deserialize<ImageButtonWidgetConfig>(options)!,
            WidgetTypes.Bar => element.Deserialize<BarWidgetConfig>(options)!,
            WidgetTypes.Image => element.Deserialize<ImageWidgetConfig>(options)!,
            _ => new UnknownWidgetConfig(type, element.Clone()),
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, WidgetConfig value, JsonSerializerOptions options)
    {
        if (value is UnknownWidgetConfig unknown)
        {
            unknown.Raw.WriteTo(writer);
            return;
        }

        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
