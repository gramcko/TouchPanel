using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchPanel.Config.Json;

/// <summary>
/// DD-9 explicit action polymorphism: reads the <c>type</c> discriminator and deserializes the
/// matching concrete <see cref="ActionConfig"/> type. Unrecognized discriminator values are not an
/// error; they become <see cref="UnknownActionConfig"/> entries so validation can disable the
/// bound widget (RF-047) while preserving the original JSON.
/// </summary>
public sealed class ActionConfigJsonConverter : JsonConverter<ActionConfig>
{
    /// <inheritdoc/>
    public override ActionConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("An action entry must be a JSON object.");
        }

        if (!element.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("An action entry requires a string 'type' discriminator.");
        }

        var type = typeElement.GetString()!;
        return type switch
        {
            ActionTypes.RunCommand => element.Deserialize<RunCommandActionConfig>(options)!,
            ActionTypes.MediaKey => element.Deserialize<MediaKeyActionConfig>(options)!,
            ActionTypes.Volume => element.Deserialize<VolumeActionConfig>(options)!,
            ActionTypes.Mute => element.Deserialize<MuteActionConfig>(options)!,
            ActionTypes.SendKeys => element.Deserialize<SendKeysActionConfig>(options)!,
            ActionTypes.Hotkey => element.Deserialize<RunHotkeyActionConfig>(options)!,
            _ => new UnknownActionConfig(type, element.Clone()),
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ActionConfig value, JsonSerializerOptions options)
    {
        if (value is UnknownActionConfig unknown)
        {
            unknown.Raw.WriteTo(writer);
            return;
        }

        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
