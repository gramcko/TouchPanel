using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;

namespace TouchPanel.Tests.Config;

public class ConfigJsonDiscriminatorTests
{
    [Fact]
    public void UnknownWidgetType_IsRepresentableWithTypeAndRawJson()
    {
        const string json = """
            { "type": "unknownWidgetType", "id": "w9", "x": 5, "history": [1, 2, 3] }
            """;

        var widget = Assert.IsType<UnknownWidgetConfig>(DeserializeWidget(json));

        Assert.Equal("unknownWidgetType", widget.Type);
        Assert.Equal("w9", widget.Id);
        Assert.Equal("unknownWidgetType", widget.Raw.GetProperty("type").GetString());
        Assert.Equal(3, widget.Raw.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public void UnknownWidgetType_SerializesBackWithOriginalJson()
    {
        const string json = """
            { "type": "unknownWidgetType", "id": "w9", "history": [1, 2, 3] }
            """;
        var page = new PageConfig { Id = "main", Widgets = [DeserializeWidget(json)] };

        var serialized = JsonSerializer.Serialize(page, ConfigJson.Options);

        using var document = JsonDocument.Parse(serialized);
        var widget = document.RootElement.GetProperty("widgets")[0];
        Assert.Equal("unknownWidgetType", widget.GetProperty("type").GetString());
        Assert.Equal("w9", widget.GetProperty("id").GetString());
        Assert.Equal(3, widget.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public void UnknownActionType_OnKnownWidget_IsPreserved()
    {
        const string json = """
            {
              "type": "button",
              "id": "b1",
              "action": { "type": "http", "url": "https://example.test/hook", "timeout": 10 }
            }
            """;

        var widget = Assert.IsType<ButtonWidgetConfig>(DeserializeWidget(json));
        var action = Assert.IsType<UnknownActionConfig>(widget.Action);

        Assert.Equal(WidgetTypes.Button, widget.Type);
        Assert.Equal("http", action.Type);
        Assert.Equal("https://example.test/hook", action.Raw.GetProperty("url").GetString());
    }

    [Fact]
    public void UnknownActionType_SerializesBackWithOriginalJson()
    {
        const string json = """
            { "type": "macro", "steps": [ { "type": "delay", "ms": 500 } ] }
            """;

        var serialized = JsonSerializer.Serialize(DeserializeAction(json), ConfigJson.Options);

        using var document = JsonDocument.Parse(serialized);
        Assert.Equal("macro", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public void WidgetWithoutTypeDiscriminator_ThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(() => DeserializeWidget("""{ "id": "b1" }"""));

        Assert.Contains("type", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WidgetWithNonStringType_ThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(() => DeserializeWidget("""{ "type": 5 }"""));

        Assert.Contains("type", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WidgetThatIsNotAnObject_ThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(() => DeserializeWidget("[1, 2, 3]"));

        Assert.Contains("object", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActionWithoutTypeDiscriminator_ThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(() => DeserializeAction("""{ "key": "next" }"""));

        Assert.Contains("type", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActionThatIsNotAnObject_ThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(() => DeserializeAction("\"mediaKey\""));

        Assert.Contains("object", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownEnumValue_MapsToUnknownSentinel()
    {
        var media = Assert.IsType<MediaKeyActionConfig>(
            DeserializeAction("""{ "type": "mediaKey", "key": "banana" }"""));
        var volume = Assert.IsType<VolumeActionConfig>(
            DeserializeAction("""{ "type": "volume", "mode": "sideways", "direction": "diagonal" }"""));

        Assert.Equal(MediaKeyKind.Unknown, media.Key);
        Assert.Equal(VolumeMode.Unknown, volume.Mode);
        Assert.Equal(VolumeDirection.Unknown, volume.Direction);
    }

    [Fact]
    public void UnknownMonitorFallback_MapsToUnknownSentinel()
    {
        const string json = """
            { "schemaVersion": 1, "monitor": { "fallback": "banana" } }
            """;

        var config = JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.Equal(MonitorFallback.Unknown, config.Monitor.Fallback);
    }

    [Fact]
    public void EnumValues_AreReadCaseInsensitively()
    {
        var media = Assert.IsType<MediaKeyActionConfig>(
            DeserializeAction("""{ "type": "mediaKey", "key": "PlayPause" }"""));

        Assert.Equal(MediaKeyKind.PlayPause, media.Key);
    }

    [Fact]
    public void NonStringEnumValue_ThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(
            () => DeserializeAction("""{ "type": "mediaKey", "key": 2 }"""));

        Assert.Contains("string", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnumValues_SerializeAsCamelCaseStrings()
    {
        var json = JsonSerializer.Serialize<ActionConfig>(
            new MediaKeyActionConfig { Key = MediaKeyKind.PlayPause },
            ConfigJson.Options);

        Assert.Contains("\"type\": \"mediaKey\"", json);
        Assert.Contains("\"key\": \"playPause\"", json);
    }

    private static WidgetConfig DeserializeWidget(string json) =>
        JsonSerializer.Deserialize<WidgetConfig>(json, ConfigJson.Options)!;

    private static ActionConfig DeserializeAction(string json) =>
        JsonSerializer.Deserialize<ActionConfig>(json, ConfigJson.Options)!;
}
