using System.Globalization;
using TouchPanel.Config;

namespace TouchPanel.Actions;

/// <summary>
/// Pure friendly-name → Win32 virtual-key mapping and validation for the send-keys action (RF-057,
/// M2-RF-571/RF-575). No input is synthesized here and no native type is referenced, so the mapping
/// is unit-testable in isolation (M2-RNF-124); <c>SendKeysHandler</c> feeds the resulting virtual
/// keys to the <see cref="IKeyInput"/> seam.
/// </summary>
/// <remarks>
/// <para><b>Fixed catalog (M2-RF-571).</b> Keys: letters <c>A</c>–<c>Z</c>, digits <c>0</c>–<c>9</c>,
/// function keys <c>F1</c>–<c>F24</c>, and the documented named keys <c>Tab</c>, <c>Enter</c>,
/// <c>Escape</c>, <c>Space</c>, <c>Left</c>, <c>Right</c>, <c>Up</c>, <c>Down</c>, <c>Home</c>,
/// <c>End</c>, <c>PageUp</c>, <c>PageDown</c>, <c>Insert</c>, <c>Delete</c>, <c>Backspace</c>.
/// Modifiers: <c>Ctrl</c>, <c>Alt</c>, <c>Shift</c>, <c>Win</c>. Matching is case-insensitive.</para>
/// <para><b>Validation (M2-RF-575).</b> An unknown key name or unknown modifier is invalid, as is a
/// missing/modifier-only combination. A bare key with no modifier is valid for a widget action; only
/// global hotkeys require a modifier (D-M2-4, enforced in T-09). Invalid entries disable the widget
/// at validation (RF-047).</para>
/// </remarks>
public sealed class SendKeyCombination
{
    /// <summary>The fixed modifier names in canonical press order (release order is the reverse).</summary>
    private static readonly (string Name, ushort VirtualKey)[] ModifierCatalog =
    [
        ("Ctrl", 0x11),   // VK_CONTROL
        ("Alt", 0x12),    // VK_MENU
        ("Shift", 0x10),  // VK_SHIFT
        ("Win", 0x5B),    // VK_LWIN
    ];

    /// <summary>The documented named keys and their Win32 virtual-key codes (M2-RF-571).</summary>
    private static readonly (string Name, ushort VirtualKey)[] NamedKeyCatalog =
    [
        ("Backspace", 0x08),
        ("Tab", 0x09),
        ("Enter", 0x0D),
        ("Escape", 0x1B),
        ("Space", 0x20),
        ("PageUp", 0x21),
        ("PageDown", 0x22),
        ("End", 0x23),
        ("Home", 0x24),
        ("Left", 0x25),
        ("Up", 0x26),
        ("Right", 0x27),
        ("Down", 0x28),
        ("Insert", 0x2D),
        ("Delete", 0x2E),
    ];

    /// <summary>
    /// The full friendly key catalog the runtime mapping and the hotkey editor share (M2-RF-571,
    /// M3-RF-612): letters <c>A</c>–<c>Z</c>, digits <c>0</c>–<c>9</c>, function keys
    /// <c>F1</c>–<c>F24</c>, then the named keys in canonical order. Exposing it keeps the editor's
    /// key selector from drifting from <see cref="TryMapKey"/>.
    /// </summary>
    public static IReadOnlyList<string> FriendlyKeyNames { get; } = BuildKeyCatalog();

    /// <summary>
    /// The four known modifier names in canonical order (<c>Ctrl</c>, <c>Alt</c>, <c>Shift</c>,
    /// <c>Win</c>), shared with the hotkey editor's modifier toggles (M3-RF-612).
    /// </summary>
    public static IReadOnlyList<string> KnownModifierNames { get; } =
        [.. ModifierCatalog.Select(entry => entry.Name)];

    private readonly string[] _modifierNames;
    private readonly ushort[] _modifierVirtualKeys;

    private SendKeyCombination(
        string keyName,
        ushort virtualKey,
        string[] modifierNames,
        ushort[] modifierVirtualKeys)
    {
        KeyName = keyName;
        VirtualKey = virtualKey;
        _modifierNames = modifierNames;
        _modifierVirtualKeys = modifierVirtualKeys;
    }

    /// <summary>Gets the configured key's canonical friendly name (for example <c>A</c> or <c>F5</c>).</summary>
    public string KeyName { get; }

    /// <summary>Gets the target key's Win32 virtual-key code.</summary>
    public ushort VirtualKey { get; }

    /// <summary>
    /// Gets the modifier names in canonical press order (<c>Ctrl</c>, <c>Alt</c>, <c>Shift</c>,
    /// <c>Win</c>); empty for a bare key.
    /// </summary>
    public IReadOnlyList<string> ModifierNames => _modifierNames;

    /// <summary>
    /// Gets the modifier virtual-key codes in canonical press order; the handler releases them in
    /// reverse order.
    /// </summary>
    public IReadOnlyList<ushort> ModifierVirtualKeys => _modifierVirtualKeys;

    /// <summary>Determines whether <paramref name="name"/> is one of the four known modifiers.</summary>
    /// <param name="name">The candidate modifier name.</param>
    /// <returns><see langword="true"/> when the name is <c>Ctrl</c>, <c>Alt</c>, <c>Shift</c>, or <c>Win</c>.</returns>
    public static bool IsKnownModifier(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        foreach (var (modifierName, _) in ModifierCatalog)
        {
            if (string.Equals(modifierName, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Maps a friendly key name to its Win32 virtual-key code (M2-RF-571).</summary>
    /// <param name="name">The friendly key name (letters, digits, <c>F1</c>–<c>F24</c>, or a named key).</param>
    /// <param name="virtualKey">The resolved virtual-key code when mapping succeeds; 0 otherwise.</param>
    /// <returns><see langword="true"/> when the name is part of the fixed catalog.</returns>
    public static bool TryMapKey(string? name, out ushort virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();

        if (trimmed.Length == 1)
        {
            var character = char.ToUpperInvariant(trimmed[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                // VK codes for A–Z and 0–9 match their ASCII values.
                virtualKey = character;
                return true;
            }
        }

        if (trimmed.Length is 2 or 3
            && (trimmed[0] is 'F' or 'f')
            && int.TryParse(trimmed.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            // VK_F1 = 0x70 … VK_F24 = 0x87.
            virtualKey = (ushort)(0x70 + (functionNumber - 1));
            return true;
        }

        foreach (var (keyName, code) in NamedKeyCatalog)
        {
            if (string.Equals(keyName, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                virtualKey = code;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses and validates a send-keys configuration into a ready-to-execute combination
    /// (M2-RF-571/RF-575): a known key (required) plus zero or more known modifiers.
    /// </summary>
    /// <param name="config">The raw send-keys action configuration.</param>
    /// <param name="combination">The parsed combination when validation succeeds; otherwise <see langword="null"/>.</param>
    /// <param name="error">A redaction-safe reason when validation fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the configuration is a valid combination.</returns>
    public static bool TryParse(
        SendKeysActionConfig? config,
        out SendKeyCombination? combination,
        out string? error)
    {
        combination = null;

        if (config is null)
        {
            error = "The send-keys action is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.Key))
        {
            error = "The send-keys action has no key.";
            return false;
        }

        if (!TryMapKey(config.Key, out var virtualKey))
        {
            error = $"Unknown key name '{config.Key.Trim()}'.";
            return false;
        }

        // Deduplicate while rejecting unknown/empty modifiers (M2-RF-575).
        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var modifier in config.Modifiers)
        {
            if (string.IsNullOrWhiteSpace(modifier))
            {
                error = "The send-keys action has an empty modifier.";
                return false;
            }

            if (!IsKnownModifier(modifier))
            {
                error = $"Unknown modifier '{modifier.Trim()}'.";
                return false;
            }

            requested.Add(modifier.Trim());
        }

        // Emit modifiers in canonical order so press/release ordering is deterministic.
        var modifierNames = new List<string>(requested.Count);
        var modifierVirtualKeys = new List<ushort>(requested.Count);
        foreach (var (name, code) in ModifierCatalog)
        {
            if (requested.Contains(name))
            {
                modifierNames.Add(name);
                modifierVirtualKeys.Add(code);
            }
        }

        combination = new SendKeyCombination(
            config.Key.Trim(),
            virtualKey,
            [.. modifierNames],
            [.. modifierVirtualKeys]);
        error = null;
        return true;
    }

    private static string[] BuildKeyCatalog()
    {
        var names = new List<string>(26 + 10 + 24 + NamedKeyCatalog.Length);

        for (var character = 'A'; character <= 'Z'; character++)
        {
            names.Add(character.ToString());
        }

        for (var digit = '0'; digit <= '9'; digit++)
        {
            names.Add(digit.ToString());
        }

        for (var function = 1; function <= 24; function++)
        {
            names.Add($"F{function}");
        }

        foreach (var (name, _) in NamedKeyCatalog)
        {
            names.Add(name);
        }

        return [.. names];
    }
}
