using TouchPanel.Actions;
using TouchPanel.Config;

namespace TouchPanel.Input;

/// <summary>
/// Pure validation/parsing of a configured hotkey into a <see cref="HotKeyBinding"/> (RF-058,
/// M2-RF-581/M2-RF-594). No native type is referenced and nothing is registered here, so the rules
/// are unit-testable in isolation (M2-RNF-124).
/// </summary>
/// <remarks>
/// <para><b>Rules (M2-RF-581/M2-RF-594, D-M2-4).</b> A hotkey must have a known key name, at least
/// one known modifier (a modifier-less entry is invalid so it can never hijack a bare key from the
/// foreground application), and exactly one bound action. Unknown keys/modifiers, an empty
/// modifier, or a missing binding are all rejected; the caller skips the entry and continues with
/// the others.</para>
/// <para>The friendly key/modifier catalogs are shared with the send-keys action so the two input
/// surfaces cannot drift (RF-057/M2-RF-571).</para>
/// </remarks>
public static class HotKeyParser
{
    /// <summary>Modifier names in canonical order, paired with their Win32 flags.</summary>
    private static readonly (string Name, HotKeyModifiers Flag)[] ModifierFlags =
    [
        ("Ctrl", HotKeyModifiers.Control),
        ("Alt", HotKeyModifiers.Alt),
        ("Shift", HotKeyModifiers.Shift),
        ("Win", HotKeyModifiers.Win),
    ];

    /// <summary>
    /// Parses and validates one configured hotkey.
    /// </summary>
    /// <param name="config">The raw hotkey configuration, or <see langword="null"/>.</param>
    /// <param name="registrationId">The numeric registration id to associate with a successful binding; must be positive.</param>
    /// <param name="binding">The parsed binding when validation succeeds; otherwise <see langword="null"/>.</param>
    /// <param name="error">A redaction-safe reason when validation fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the entry is a valid, registerable hotkey.</returns>
    public static bool TryParse(
        HotkeyConfig? config,
        int registrationId,
        out HotKeyBinding? binding,
        out string? error)
    {
        binding = null;

        if (config is null)
        {
            error = "The hotkey entry is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.Key))
        {
            error = "The hotkey has no key.";
            return false;
        }

        if (!SendKeyCombination.TryMapKey(config.Key, out var virtualKey))
        {
            error = $"Unknown key name '{config.Key.Trim()}'.";
            return false;
        }

        if (config.Modifiers.Count == 0)
        {
            error = "The hotkey must include at least one modifier (Ctrl, Alt, Shift, or Win).";
            return false;
        }

        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var modifier in config.Modifiers)
        {
            if (string.IsNullOrWhiteSpace(modifier))
            {
                error = "The hotkey has an empty modifier.";
                return false;
            }

            if (!SendKeyCombination.IsKnownModifier(modifier))
            {
                error = $"Unknown modifier '{modifier.Trim()}'.";
                return false;
            }

            requested.Add(modifier.Trim());
        }

        if (config.Action is null)
        {
            error = "The hotkey has no bound action.";
            return false;
        }

        // Build the flags and the display string in canonical order so press/registration and logs
        // are deterministic regardless of the configured modifier order (M2-RF-593).
        var flags = HotKeyModifiers.NoRepeat;
        var parts = new List<string>(requested.Count);
        foreach (var (name, flag) in ModifierFlags)
        {
            if (requested.Contains(name))
            {
                flags |= flag;
                parts.Add(name);
            }
        }

        var keyName = config.Key.Trim();
        var combination = $"{string.Join('+', parts)}+{keyName}";
        var configId = string.IsNullOrWhiteSpace(config.Id) ? $"hotkey-{registrationId}" : config.Id;

        binding = new HotKeyBinding(
            registrationId,
            configId,
            keyName,
            virtualKey,
            flags,
            combination,
            config.Action);
        error = null;
        return true;
    }
}
