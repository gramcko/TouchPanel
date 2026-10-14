using TouchPanel.Config;
using TouchPanel.Input;

namespace TouchPanel.Tests.Input;

/// <summary>
/// RF-058/M2-RF-581/M2-RF-594 parser behavior: known key/modifier mapping, the mandatory
/// at-least-one-modifier rule (D-M2-4), canonical modifier ordering/deduplication, and rejection of
/// invalid entries so the service can skip them and continue.
/// </summary>
public sealed class HotKeyParserTests
{
    private const ushort VkControl = 0x11;
    private const ushort VkAlt = 0x12;
    private const ushort VkShift = 0x10;
    private const ushort VkWin = 0x5B;
    private const ushort VkA = 0x41;
    private const ushort VkF5 = 0x74;
    private const ushort VkTab = 0x09;

    [Fact]
    public void TryParse_ValidLetterWithModifier_MapsFlagsAndVirtualKey()
    {
        var success = HotKeyParser.TryParse(Config("A", "Ctrl"), registrationId: 1, out var binding, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(binding);
        Assert.Equal(VkA, binding!.VirtualKey);
        Assert.Equal("A", binding.KeyName);
        Assert.Equal("Ctrl+A", binding.Combination);
        Assert.True(binding.Modifiers.HasFlag(HotKeyModifiers.Control));
        Assert.True(binding.Modifiers.HasFlag(HotKeyModifiers.NoRepeat));
        Assert.False(binding.Modifiers.HasFlag(HotKeyModifiers.Alt));
    }

    [Fact]
    public void TryParse_Modifiers_AreCanonicalizedAndDeduplicated()
    {
        var success = HotKeyParser.TryParse(
            Config("A", "Alt", "ctrl", "CTRL"),
            registrationId: 7,
            out var binding,
            out _);

        Assert.True(success);
        Assert.Equal("Ctrl+Alt+A", binding!.Combination);
        Assert.True(binding.Modifiers.HasFlag(HotKeyModifiers.Control));
        Assert.True(binding.Modifiers.HasFlag(HotKeyModifiers.Alt));
    }

    [Fact]
    public void TryParse_AllModifiers_SetsEveryFlag()
    {
        var success = HotKeyParser.TryParse(
            Config("A", "Ctrl", "Alt", "Shift", "Win"),
            registrationId: 1,
            out var binding,
            out _);

        Assert.True(success);
        Assert.Equal(
            HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift | HotKeyModifiers.Win | HotKeyModifiers.NoRepeat,
            binding!.Modifiers);
    }

    [Theory]
    [InlineData("F5", VkF5)]
    [InlineData("Tab", VkTab)]
    [InlineData("f24", 0x87)]
    public void TryParse_NamedAndFunctionKeys_MapToTheVirtualKey(string key, ushort expected)
    {
        var success = HotKeyParser.TryParse(Config(key, "Alt"), registrationId: 1, out var binding, out _);

        Assert.True(success);
        Assert.Equal(expected, binding!.VirtualKey);
    }

    [Fact]
    public void TryParse_NoModifier_IsInvalid()
    {
        var success = HotKeyParser.TryParse(Config("A"), registrationId: 1, out var binding, out var error);

        Assert.False(success);
        Assert.Null(binding);
        Assert.Contains("modifier", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_UnknownKey_IsInvalid()
    {
        var success = HotKeyParser.TryParse(Config("Nope", "Ctrl"), registrationId: 1, out _, out var error);

        Assert.False(success);
        Assert.Contains("Unknown key", error);
    }

    [Fact]
    public void TryParse_UnknownModifier_IsInvalid()
    {
        var success = HotKeyParser.TryParse(Config("A", "Meta"), registrationId: 1, out _, out var error);

        Assert.False(success);
        Assert.Contains("Unknown modifier", error);
    }

    [Fact]
    public void TryParse_EmptyModifier_IsInvalid()
    {
        var success = HotKeyParser.TryParse(Config("A", "  "), registrationId: 1, out _, out var error);

        Assert.False(success);
        Assert.Contains("empty modifier", error);
    }

    [Fact]
    public void TryParse_MissingKey_IsInvalid()
    {
        var success = HotKeyParser.TryParse(Config(null, "Ctrl"), registrationId: 1, out _, out var error);

        Assert.False(success);
        Assert.Contains("no key", error);
    }

    [Fact]
    public void TryParse_MissingBinding_IsInvalid()
    {
        var config = Config("A", "Ctrl");
        config.Action = null;

        var success = HotKeyParser.TryParse(config, registrationId: 1, out _, out var error);

        Assert.False(success);
        Assert.Contains("bound action", error);
    }

    [Fact]
    public void TryParse_NullConfig_IsInvalid()
    {
        Assert.False(HotKeyParser.TryParse(null, registrationId: 1, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_MissingId_SynthesizesAConfigIdFromTheRegistrationId()
    {
        var success = HotKeyParser.TryParse(Config("A", "Ctrl"), registrationId: 3, out var binding, out _);

        Assert.True(success);
        Assert.Equal("hotkey-3", binding!.ConfigId);
    }

    [Fact]
    public void TryParse_ConfiguredId_IsPreserved()
    {
        var config = Config("A", "Ctrl");
        config.Id = "my-hotkey";

        var success = HotKeyParser.TryParse(config, registrationId: 1, out var binding, out _);

        Assert.True(success);
        Assert.Equal("my-hotkey", binding!.ConfigId);
    }

    private static HotkeyConfig Config(string? key, params string[] modifiers) => new()
    {
        Id = string.Empty,
        Key = key,
        Modifiers = [.. modifiers],
        Action = new SendKeysActionConfig { Key = "B", Modifiers = ["Alt"] },
    };
}
