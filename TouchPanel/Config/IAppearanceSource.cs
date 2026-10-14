namespace TouchPanel.Config;

/// <summary>
/// Application-level appearance state the widget view models read (M3-RF-844/M3-RF-845): the active
/// built-in icon-pack id plus a change notification so realized widgets re-resolve their glyphs when
/// the pack changes, without a rebuild. This type is WPF-free so the resolution logic stays
/// unit-testable; the composition root owns the mutable implementation and updates it as the
/// appearance apply step runs (M3-RF-848/M3-RF-850).
/// </summary>
public interface IAppearanceSource
{
    /// <summary>
    /// Gets the canonical id of the active built-in icon pack (always a valid
    /// <see cref="IconPackIds"/> value).
    /// </summary>
    string ActiveIconPackId { get; }

    /// <summary>
    /// Raised after the active appearance (icon pack) changed. Consumers re-resolve their derived
    /// state on the UI thread; the event carries no payload beyond the source itself.
    /// </summary>
    event EventHandler? AppearanceChanged;
}
