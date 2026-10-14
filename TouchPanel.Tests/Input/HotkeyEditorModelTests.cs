using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Input;

namespace TouchPanel.Tests.Input;

/// <summary>
/// WPF-free hotkey editor model (M3-RF-611..613/M3-RF-619): listing, add (generated unique id),
/// edit, remove, dirty tracking, validation through <see cref="HotKeyParser"/>/
/// <see cref="SendKeyCombination"/> (at least one modifier, known key/modifier, exactly one action),
/// and duplicate detection with first-wins semantics. No registration or native type is involved
/// (M3-RNF-133).
/// </summary>
public sealed class HotkeyEditorModelTests
{
    [Fact]
    public void Constructor_SeedsEntriesFromConfigurationInOrder()
    {
        var model = new HotkeyEditorModel(
        [
            Config("A", ["Ctrl"], action: new MuteActionConfig()),
            Config("S", ["Alt", "Shift"], action: new MediaKeyActionConfig()),
        ]);

        Assert.Equal(2, model.Entries.Count);
        Assert.Equal("A", model.Entries[0].Key);
        Assert.Equal("Ctrl+A", model.Entries[0].Combination);
        Assert.Equal("Alt+Shift+S", model.Entries[1].Combination);
        Assert.All(model.Entries, entry => Assert.Equal(HotkeyEntryStatus.Valid, entry.Status));
        Assert.False(model.IsDirty);
        Assert.True(model.IsValid);
    }

    [Fact]
    public void Add_GeneratesUniqueIdAndMarksDirty()
    {
        var model = new HotkeyEditorModel();

        var first = model.Add("A", ["Ctrl"], new MuteActionConfig());
        var second = model.Add("S", ["Alt"], new MuteActionConfig());

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.Entry!.Id, second.Entry!.Id);
        Assert.True(model.IsDirty);
        Assert.Equal(2, model.Entries.Count);
    }

    [Fact]
    public void Add_WithoutModifier_IsRejectedInlineAndUnchanged()
    {
        var model = new HotkeyEditorModel();

        var result = model.Add("A", [], new MuteActionConfig());

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Empty(model.Entries);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void Add_UnknownKey_IsRejected()
    {
        var model = new HotkeyEditorModel();

        var result = model.Add("NotAKey", ["Ctrl"], new MuteActionConfig());

        Assert.False(result.Succeeded);
        Assert.Contains("NotAKey", result.Error);
        Assert.Empty(model.Entries);
    }

    [Fact]
    public void Add_WithoutAction_IsRejected()
    {
        var model = new HotkeyEditorModel();

        var result = model.Add("A", ["Ctrl"], action: null);

        Assert.False(result.Succeeded);
        Assert.Contains("action", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(model.Entries);
    }

    [Fact]
    public void Add_DuplicateCombination_FirstWinsAndSecondIsRejected()
    {
        var model = new HotkeyEditorModel();
        var first = model.Add("A", ["Ctrl"], new MuteActionConfig());

        // Same normalized modifier set + key, case-insensitive.
        var duplicate = model.Add("a", ["ctrl"], new MuteActionConfig());

        Assert.True(first.Succeeded);
        Assert.False(duplicate.Succeeded);
        Assert.Contains("already used", duplicate.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(model.Entries);
        Assert.Equal("Ctrl+A", model.Entries[0].Combination);
    }

    [Fact]
    public void Edit_UpdatesTheEntryAndMarksDirty()
    {
        var model = new HotkeyEditorModel([Config("A", ["Ctrl"], id: "hotkey-1")]);

        var result = model.Edit("hotkey-1", "S", ["Shift"], new MediaKeyActionConfig());

        Assert.True(result.Succeeded);
        Assert.Equal("Shift+S", model.Entries[0].Combination);
        Assert.IsType<MediaKeyActionConfig>(model.Entries[0].Action);
        Assert.True(model.IsDirty);
    }

    [Fact]
    public void Edit_ToADuplicate_IsRejectedAndLeavesTheEntryUnchanged()
    {
        var model = new HotkeyEditorModel(
        [
            Config("A", ["Ctrl"], id: "hotkey-1"),
            Config("S", ["Alt"], id: "hotkey-2"),
        ]);

        var result = model.Edit("hotkey-2", "A", ["Ctrl"], new MuteActionConfig());

        Assert.False(result.Succeeded);
        Assert.Contains("already used", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Alt+S", model.Entries[1].Combination);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void Edit_UnknownId_IsRejected()
    {
        var model = new HotkeyEditorModel();

        var result = model.Edit("missing", "A", ["Ctrl"], new MuteActionConfig());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Remove_DropsTheEntry()
    {
        var model = new HotkeyEditorModel([Config("A", ["Ctrl"], id: "hotkey-1")]);

        Assert.True(model.Remove("hotkey-1"));
        Assert.Empty(model.Entries);
        Assert.True(model.IsDirty);
        Assert.False(model.Remove("hotkey-1"));
    }

    [Fact]
    public void InitialDuplicateCombination_FlagsTheSecondAsDuplicateAndIsInvalid()
    {
        var model = new HotkeyEditorModel(
        [
            Config("A", ["Ctrl"], id: "hotkey-1"),
            Config("a", ["ctrl"], id: "hotkey-2"),
        ]);

        Assert.Equal(HotkeyEntryStatus.Valid, model.Entries[0].Status);
        Assert.Equal(HotkeyEntryStatus.Duplicate, model.Entries[1].Status);
        Assert.Contains("hotkey-1", model.Entries[1].Error);
        Assert.False(model.IsValid);
    }

    [Fact]
    public void ToConfigList_RoundTripsThroughTheRuntimeParser()
    {
        var model = new HotkeyEditorModel();
        model.Add("F5", ["Ctrl", "Alt"], new MuteActionConfig());

        var configs = model.ToConfigList();

        var config = Assert.Single(configs);
        Assert.True(HotKeyParser.TryParse(config, 1, out var binding, out var error), error);
        Assert.Equal("Ctrl+Alt+F5", binding!.Combination);
    }

    [Fact]
    public void Add_CombinationUsesCanonicalModifierOrder()
    {
        var model = new HotkeyEditorModel();

        var result = model.Add("A", ["Shift", "Ctrl"], new MuteActionConfig());

        Assert.Equal("Ctrl+Shift+A", result.Entry!.Combination);
    }

    [Fact]
    public void MarkSaved_ClearsDirty()
    {
        var model = new HotkeyEditorModel();
        model.Add("A", ["Ctrl"], new MuteActionConfig());
        Assert.True(model.IsDirty);

        model.MarkSaved();

        Assert.False(model.IsDirty);
    }

    [Fact]
    public void Reload_ReplacesTheWorkingCopyAndClearsDirty()
    {
        var model = new HotkeyEditorModel([Config("A", ["Ctrl"], id: "hotkey-1")]);
        model.Add("S", ["Alt"], new MuteActionConfig());

        model.Reload([Config("B", ["Ctrl"], id: "hotkey-9")]);

        var entry = Assert.Single(model.Entries);
        Assert.Equal("B", entry.Key);
        Assert.Equal("hotkey-9", entry.Id);
        Assert.False(model.IsDirty);
    }

    private static HotkeyConfig Config(string key, string[] modifiers, string? id = null, ActionConfig? action = null) =>
        new()
        {
            Id = id ?? string.Empty,
            Key = key,
            Modifiers = [.. modifiers],
            Action = action ?? new MuteActionConfig(),
        };
}
