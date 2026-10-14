using System.ComponentModel;
using System.Runtime.CompilerServices;
using TouchPanel.Actions;
using TouchPanel.Config;

namespace TouchPanel.Input;

/// <summary>Validation state of one editable hotkey entry (M3-RF-613).</summary>
public enum HotkeyEntryStatus
{
    /// <summary>The entry is a valid, registerable hotkey and no earlier entry uses the same combination.</summary>
    Valid,

    /// <summary>The entry violates a runtime registration rule and cannot be written to the configuration.</summary>
    Invalid,

    /// <summary>The entry's combination is identical to an earlier entry's; the first entry wins (M2-RF-593).</summary>
    Duplicate,
}

/// <summary>
/// One editable global-hotkey entry exposed by <see cref="HotkeyEditorModel"/> (M3-RF-612/M3-RF-613).
/// It is a plain WPF-free data object; the model keeps <see cref="Status"/>, <see cref="Error"/>, and
/// <see cref="Combination"/> current and notifies the presentation on change so the editor's list
/// stays live. Nothing here is registered with the operating system (M3-RNF-133).
/// </summary>
public sealed class HotkeyEditorEntry : INotifyPropertyChanged
{
    private string _key;
    private IReadOnlyList<string> _modifiers;
    private ActionConfig? _action;
    private string _combination;
    private HotkeyEntryStatus _status = HotkeyEntryStatus.Invalid;
    private string? _error;

    internal HotkeyEditorEntry(
        string id,
        string key,
        IReadOnlyList<string> modifiers,
        ActionConfig? action)
    {
        Id = id;
        _key = key ?? string.Empty;
        _modifiers = modifiers;
        _action = action;
        _combination = HotkeyEditorModel.BuildCombination(_key, _modifiers);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the unique entry id (preserved from configuration; generated for new entries).</summary>
    public string Id { get; internal set; }

    /// <summary>Gets the friendly key name (for example <c>A</c> or <c>F5</c>); empty when unconfigured.</summary>
    public string Key
    {
        get => _key;
        internal set => SetField(ref _key, value ?? string.Empty);
    }

    /// <summary>Gets the modifier names; order is preserved as entered, with known names canonicalized for display.</summary>
    public IReadOnlyList<string> Modifiers
    {
        get => _modifiers;
        internal set => SetField(ref _modifiers, value);
    }

    /// <summary>Gets the single bound action, or <see langword="null"/> when unconfigured (M3-RF-613).</summary>
    public ActionConfig? Action
    {
        get => _action;
        internal set
        {
            if (SetField(ref _action, value))
            {
                OnPropertyChanged(nameof(ActionSummary));
            }
        }
    }

    /// <summary>Gets the human-readable combination in canonical modifier order (for example <c>Ctrl+Alt+A</c>).</summary>
    public string Combination
    {
        get => _combination;
        internal set => SetField(ref _combination, value);
    }

    /// <summary>Gets the validation state after the last model mutation (M3-RF-613).</summary>
    public HotkeyEntryStatus Status
    {
        get => _status;
        internal set => SetField(ref _status, value);
    }

    /// <summary>Gets the inline validation reason when the entry is invalid or a duplicate; otherwise <see langword="null"/>.</summary>
    public string? Error
    {
        get => _error;
        internal set
        {
            if (SetField(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>Gets a value indicating whether the entry can be written to the configuration.</summary>
    public bool IsValid => _status == HotkeyEntryStatus.Valid;

    /// <summary>Gets a value indicating whether an inline error is present.</summary>
    public bool HasError => !string.IsNullOrEmpty(_error);

    /// <summary>Gets a short, human-readable summary of the bound action for the list.</summary>
    public string ActionSummary => HotkeyActionSummary.Describe(_action);

    internal HotkeyConfig ToConfig() => new()
    {
        Id = Id,
        Key = Key,
        Modifiers = [.. Modifiers],
        Action = Action,
    };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Outcome of an add/edit/remove operation on <see cref="HotkeyEditorModel"/> (M3-RF-612/M3-RF-613).
/// A rejected operation carries the inline reason and leaves the model unchanged.
/// </summary>
/// <param name="Succeeded">Whether the operation changed the model.</param>
/// <param name="Entry">The added or updated entry on success; otherwise <see langword="null"/>.</param>
/// <param name="Error">The inline reason on failure; otherwise <see langword="null"/>.</param>
public sealed record HotkeyEditResult(bool Succeeded, HotkeyEditorEntry? Entry, string? Error)
{
    /// <summary>Creates a success result for an added or updated entry.</summary>
    /// <param name="entry">The affected entry.</param>
    /// <returns>The result.</returns>
    public static HotkeyEditResult Accepted(HotkeyEditorEntry entry) => new(true, entry, null);

    /// <summary>Creates a rejection result carrying the inline reason.</summary>
    /// <param name="error">The redaction-safe reason.</param>
    /// <returns>The result.</returns>
    public static HotkeyEditResult Rejected(string error) => new(false, null, error);
}

/// <summary>
/// WPF-free editing model for the global <c>hotkeys</c> list (RF-061/M3-RF-611..613/M3-RF-619). It
/// lists, adds (with a generated unique id), edits, and removes entries; validates every entry with
/// the exact runtime rules by delegating to <see cref="HotKeyParser"/> and
/// <see cref="SendKeyCombination"/> (so the editor and the registrar cannot drift); and detects
/// duplicate combinations with first-wins semantics. Invalid or duplicate entries are flagged and
/// never produced for persistence (M3-RF-613).
/// </summary>
/// <remarks>
/// <para><b>Dirty tracking.</b> <see cref="IsDirty"/> is set by any mutating operation and cleared
/// by <see cref="MarkSaved"/>; callers persist only through the single-writer path (M3-RF-615).</para>
/// <para><b>Purity.</b> No native type, no WPF type, and no registration call appears here
/// (M3-RNF-133/M3-RNF-134).</para>
/// </remarks>
public sealed class HotkeyEditorModel
{
    private readonly List<HotkeyEditorEntry> _entries = [];
    private int _idSeed;
    private bool _isDirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyEditorModel"/> class from the effective
    /// configuration, seeding the working copy. Initial entries keep their configured ids; blank ids
    /// receive a generated unique one.
    /// </summary>
    /// <param name="initial">The configured hotkeys in configuration order, or <see langword="null"/> for an empty list.</param>
    public HotkeyEditorModel(IReadOnlyList<HotkeyConfig>? initial = null)
    {
        if (initial is not null)
        {
            foreach (var config in initial)
            {
                if (config is null)
                {
                    continue;
                }

                var id = string.IsNullOrWhiteSpace(config.Id) ? NextId() : config.Id.Trim();
                _entries.Add(new HotkeyEditorEntry(
                    id,
                    config.Key ?? string.Empty,
                    Clean(config.Modifiers),
                    config.Action));
            }
        }

        RefreshValidation();
    }

    /// <summary>Gets the editable entries in configuration order; the first entry with a combination wins duplicates.</summary>
    public IReadOnlyList<HotkeyEditorEntry> Entries => _entries;

    /// <summary>Gets a value indicating whether the working copy differs from the last saved/loaded state.</summary>
    public bool IsDirty => _isDirty;

    /// <summary>Gets a value indicating whether every entry is valid (no invalid and no duplicate entries).</summary>
    public bool IsValid => _entries.All(entry => entry.Status == HotkeyEntryStatus.Valid);

    /// <summary>Adds a new entry with a generated unique id, validating it first (M3-RF-612/M3-RF-613).</summary>
    /// <param name="key">The friendly key name.</param>
    /// <param name="modifiers">The modifier names; at least one is required.</param>
    /// <param name="action">The single bound action.</param>
    /// <returns>The result; on rejection the model is unchanged and the reason is returned.</returns>
    public HotkeyEditResult Add(string key, IReadOnlyList<string>? modifiers, ActionConfig? action)
    {
        var candidate = new HotkeyEditorEntry(NextId(), key ?? string.Empty, Clean(modifiers), action);
        if (ValidateCandidate(candidate, excludeId: null) is { } error)
        {
            return HotkeyEditResult.Rejected(error);
        }

        _entries.Add(candidate);
        _isDirty = true;
        RefreshValidation();
        return HotkeyEditResult.Accepted(candidate);
    }

    /// <summary>Edits an existing entry in place, validating the result first (M3-RF-612/M3-RF-613).</summary>
    /// <param name="id">The id of the entry to edit.</param>
    /// <param name="key">The new friendly key name.</param>
    /// <param name="modifiers">The new modifier names.</param>
    /// <param name="action">The new single bound action.</param>
    /// <returns>The result; on rejection the entry is unchanged and the reason is returned.</returns>
    public HotkeyEditResult Edit(string id, string key, IReadOnlyList<string>? modifiers, ActionConfig? action)
    {
        var entry = Find(id);
        if (entry is null)
        {
            return HotkeyEditResult.Rejected($"No hotkey entry with the id '{id}'.");
        }

        var candidate = new HotkeyEditorEntry(entry.Id, key ?? string.Empty, Clean(modifiers), action);
        if (ValidateCandidate(candidate, entry.Id) is { } error)
        {
            return HotkeyEditResult.Rejected(error);
        }

        entry.Key = candidate.Key;
        entry.Modifiers = candidate.Modifiers;
        entry.Action = candidate.Action;
        _isDirty = true;
        RefreshValidation();
        return HotkeyEditResult.Accepted(entry);
    }

    /// <summary>Removes the entry with the given id (M3-RF-612).</summary>
    /// <param name="id">The id of the entry to remove.</param>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    public bool Remove(string id)
    {
        var entry = Find(id);
        if (entry is null)
        {
            return false;
        }

        _entries.Remove(entry);
        _isDirty = true;
        RefreshValidation();
        return true;
    }

    /// <summary>Finds an entry by id, or <see langword="null"/> when it does not exist.</summary>
    /// <param name="id">The entry id.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    public HotkeyEditorEntry? Find(string? id) =>
        id is null ? null : _entries.Find(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Produces the validated configuration list for persistence, in configuration order. Callers
    /// must check <see cref="IsValid"/> first; the returned list is always a fresh copy so the
    /// running configuration is never aliased or mutated (M2-RF-922/M3-RF-615).
    /// </summary>
    /// <returns>A new list of hotkey configurations.</returns>
    public IReadOnlyList<HotkeyConfig> ToConfigList() =>
        _entries.Select(entry => entry.ToConfig()).ToList();

    /// <summary>Marks the working copy as persisted (clears <see cref="IsDirty"/>).</summary>
    public void MarkSaved() => _isDirty = false;

    /// <summary>
    /// Replaces the working copy with <paramref name="configs"/> and clears the dirty flag (used
    /// after an external reload or import).
    /// </summary>
    /// <param name="configs">The hotkeys to load.</param>
    public void Reload(IReadOnlyList<HotkeyConfig>? configs)
    {
        _entries.Clear();
        _idSeed = 0;
        if (configs is not null)
        {
            foreach (var config in configs)
            {
                if (config is null)
                {
                    continue;
                }

                var id = string.IsNullOrWhiteSpace(config.Id) ? NextId() : config.Id.Trim();
                _entries.Add(new HotkeyEditorEntry(
                    id,
                    config.Key ?? string.Empty,
                    Clean(config.Modifiers),
                    config.Action));
            }
        }

        _isDirty = false;
        RefreshValidation();
    }

    private string NextId()
    {
        // Generate a stable, collision-free id without depending on any external counter.
        while (true)
        {
            var candidate = $"hotkey-{++_idSeed}";
            if (_entries.TrueForAll(entry => !string.Equals(entry.Id, candidate, StringComparison.Ordinal)))
            {
                return candidate;
            }
        }
    }

    private string? ValidateCandidate(HotkeyEditorEntry candidate, string? excludeId)
    {
        if (!HotKeyParser.TryParse(candidate.ToConfig(), 1, out var binding, out var error))
        {
            return error;
        }

        foreach (var entry in _entries)
        {
            if (string.Equals(entry.Id, excludeId, StringComparison.Ordinal))
            {
                continue;
            }

            if (HotKeyParser.TryParse(entry.ToConfig(), 1, out var other, out _)
                && string.Equals(other!.Identity, binding!.Identity, StringComparison.Ordinal))
            {
                return $"The combination '{binding.Combination}' is already used by '{entry.Id}'.";
            }
        }

        return null;
    }

    private void RefreshValidation()
    {
        var seen = new Dictionary<string, HotkeyEditorEntry>(StringComparer.Ordinal);

        for (var index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            entry.Combination = BuildCombination(entry.Key, entry.Modifiers);

            if (!HotKeyParser.TryParse(entry.ToConfig(), index + 1, out var binding, out var error))
            {
                entry.Status = HotkeyEntryStatus.Invalid;
                entry.Error = error;
                continue;
            }

            entry.Combination = binding!.Combination;
            if (seen.TryGetValue(binding.Identity, out var first))
            {
                entry.Status = HotkeyEntryStatus.Duplicate;
                entry.Error = $"The combination '{binding.Combination}' is already used by '{first.Id}'.";
            }
            else
            {
                entry.Status = HotkeyEntryStatus.Valid;
                entry.Error = null;
                seen[binding.Identity] = entry;
            }
        }
    }

    internal static string BuildCombination(string key, IReadOnlyList<string> modifiers)
    {
        var parts = new List<string>(modifiers.Count);
        foreach (var name in SendKeyCombination.KnownModifierNames)
        {
            if (modifiers.Any(modifier => string.Equals(modifier, name, StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add(name);
            }
        }

        foreach (var modifier in modifiers)
        {
            if (!SendKeyCombination.IsKnownModifier(modifier) && !parts.Contains(modifier, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(modifier);
            }
        }

        var keyText = string.IsNullOrWhiteSpace(key) ? "(none)" : key.Trim();
        return parts.Count > 0 ? $"{string.Join('+', parts)}+{keyText}" : keyText;
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? modifiers)
    {
        if (modifiers is null || modifiers.Count == 0)
        {
            return [];
        }

        var result = new List<string>(modifiers.Count);
        foreach (var modifier in modifiers)
        {
            if (!string.IsNullOrWhiteSpace(modifier))
            {
                result.Add(modifier.Trim());
            }
        }

        return result;
    }
}

/// <summary>
/// Friendly action descriptions for the hotkey editor list (M3-RF-611) and send-keys summaries.
/// Inert presentation text only; it never executes or resolves an action.
/// </summary>
public static class HotkeyActionSummary
{
    /// <summary>Describes an action in one short line.</summary>
    /// <param name="action">The bound action, or <see langword="null"/>.</param>
    /// <returns>A human-readable summary.</returns>
    public static string Describe(ActionConfig? action) => action switch
    {
        null => "No action",
        MuteActionConfig => "Mute",
        MediaKeyActionConfig media => $"Media: {media.Key}",
        VolumeActionConfig { Mode: VolumeMode.Set } volume => $"Volume: {volume.Level}%",
        VolumeActionConfig { Mode: VolumeMode.Step } volume => $"Volume: step {volume.Direction}",
        VolumeActionConfig => "Volume",
        SendKeysActionConfig send => $"Send keys: {DescribeSendKeys(send)}",
        RunCommandActionConfig run => string.IsNullOrWhiteSpace(run.Path)
            ? "Run command (no path)"
            : $"Run: {run.Path}",
        _ => action.Type,
    };

    private static string DescribeSendKeys(SendKeysActionConfig send)
    {
        if (string.IsNullOrWhiteSpace(send.Key))
        {
            return "(no key)";
        }

        var parts = new List<string>(send.Modifiers.Count + 1);
        foreach (var name in SendKeyCombination.KnownModifierNames)
        {
            if (send.Modifiers.Exists(modifier => string.Equals(modifier, name, StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add(name);
            }
        }

        parts.Add(send.Key.Trim());
        return string.Join('+', parts);
    }
}
