using TouchPanel.Actions;
using TouchPanel.Config;

namespace TouchPanel.Tests.Actions;

/// <summary>
/// RF-057/M2-RF-571/RF-575 pure mapping and validation: friendly names resolve to the exact Win32
/// virtual key, unknown key/modifier names and modifier-only combinations are rejected, and a bare
/// key is valid for a widget action. No input is synthesized.
/// </summary>
public class SendKeyCombinationTests
{
    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("a", 0x41)]
    [InlineData("z", 0x5A)]
    [InlineData("0", 0x30)]
    [InlineData("9", 0x39)]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("f24", 0x87)]
    [InlineData("Tab", 0x09)]
    [InlineData("Enter", 0x0D)]
    [InlineData("Escape", 0x1B)]
    [InlineData("Space", 0x20)]
    [InlineData("Left", 0x25)]
    [InlineData("Right", 0x27)]
    [InlineData("Up", 0x26)]
    [InlineData("Down", 0x28)]
    [InlineData("Home", 0x24)]
    [InlineData("End", 0x23)]
    [InlineData("PageUp", 0x21)]
    [InlineData("PageDown", 0x22)]
    [InlineData("Insert", 0x2D)]
    [InlineData("Delete", 0x2E)]
    [InlineData("Backspace", 0x08)]
    public void TryMapKey_KnownName_ReturnsExactVirtualKey(string name, int expected)
    {
        var mapped = SendKeyCombination.TryMapKey(name, out var virtualKey);

        Assert.True(mapped);
        Assert.Equal((ushort)expected, virtualKey);
    }

    [Theory]
    [InlineData("F0")]
    [InlineData("F25")]
    [InlineData("AA")]
    [InlineData("Control")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryMapKey_UnknownName_IsRejected(string? name)
    {
        Assert.False(SendKeyCombination.TryMapKey(name, out var virtualKey));
        Assert.Equal(0, virtualKey);
    }

    [Theory]
    [InlineData("Ctrl")]
    [InlineData("ctrl")]
    [InlineData("Alt")]
    [InlineData("Shift")]
    [InlineData("Win")]
    public void IsKnownModifier_KnownName_ReturnsTrue(string name) =>
        Assert.True(SendKeyCombination.IsKnownModifier(name));

    [Theory]
    [InlineData("Control")]
    [InlineData("Windows")]
    [InlineData("Super")]
    [InlineData("Meta")]
    [InlineData("")]
    [InlineData(null)]
    public void IsKnownModifier_UnknownName_ReturnsFalse(string? name) =>
        Assert.False(SendKeyCombination.IsKnownModifier(name));

    [Fact]
    public void TryParse_BareKey_IsValidWithNoModifiers()
    {
        var parsed = SendKeyCombination.TryParse(
            new SendKeysActionConfig { Key = "F5" },
            out var combination,
            out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.NotNull(combination);
        Assert.Equal("F5", combination!.KeyName);
        Assert.Equal(0x74, combination.VirtualKey);
        Assert.Empty(combination.ModifierVirtualKeys);
        Assert.Empty(combination.ModifierNames);
    }

    [Fact]
    public void TryParse_Modifiers_AreDeduplicatedAndCanonicallyOrdered()
    {
        var parsed = SendKeyCombination.TryParse(
            new SendKeysActionConfig { Key = "S", Modifiers = ["Shift", "Ctrl", "shift"] },
            out var combination,
            out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal(["Ctrl", "Shift"], combination!.ModifierNames);
        Assert.Equal(new ushort[] { 0x11, 0x10 }, combination.ModifierVirtualKeys);
    }

    [Fact]
    public void TryParse_ModifierOnly_IsInvalid()
    {
        Assert.False(SendKeyCombination.TryParse(
            new SendKeysActionConfig { Key = "  ", Modifiers = ["Alt"] },
            out var combination,
            out var error));

        Assert.Null(combination);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_UnknownKey_IsInvalid()
    {
        Assert.False(SendKeyCombination.TryParse(
            new SendKeysActionConfig { Key = "Foo", Modifiers = ["Alt"] },
            out _,
            out var error));

        Assert.Contains("Foo", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_UnknownModifier_IsInvalid()
    {
        Assert.False(SendKeyCombination.TryParse(
            new SendKeysActionConfig { Key = "A", Modifiers = ["Hyper"] },
            out _,
            out var error));

        Assert.Contains("Hyper", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_NullConfig_IsInvalid() =>
        Assert.False(SendKeyCombination.TryParse(null, out _, out _));
}
