using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchPanel.Config.Json;

/// <summary>
/// Reads enum string values case-insensitively and maps unrecognized text to the enum's
/// <c>Unknown = 0</c> sentinel instead of throwing. This keeps a single bad enum value an
/// entry-level validation problem (disabled widget / warned fallback per RF-047, RF-079) rather
/// than escalating the whole file to the recovery path (RF-075.3).
/// </summary>
/// <typeparam name="TEnum">Enum type that declares an <c>Unknown = 0</c> member.</typeparam>
public sealed class LenientEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> CamelCaseNames = BuildCamelCaseNames();

    /// <inheritdoc/>
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Value for '{typeof(TEnum).Name}' must be a JSON string.");
        }

        var text = reader.GetString();
        return text is not null && CamelCaseNames.TryGetValue(text, out var value)
            ? value
            : default;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        var name = value.ToString();
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(name));
    }

    private static Dictionary<string, TEnum> BuildCamelCaseNames()
    {
        var names = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames<TEnum>())
        {
            names[JsonNamingPolicy.CamelCase.ConvertName(name)] = Enum.Parse<TEnum>(name);
        }

        return names;
    }
}
