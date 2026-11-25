using System.Collections.Generic;
using System.Linq;

namespace TouchPanel.Config;

/// <summary>
/// Non-user-facing runtime flags persisted in <c>state.json</c> beside <c>panel.json</c>
/// (RF-073, D-12). Keeping them out of the user-editable configuration keeps that file clean;
/// because these flags are disposable, a missing or corrupt state file is non-fatal and is
/// regenerated with defaults.
/// </summary>
public sealed class RuntimeState
{
    /// <summary>Schema version written to <c>state.json</c>; stale files are regenerated rather than migrated.</summary>
    public const int CurrentSchemaVersion = 1;

    private Dictionary<string, bool> _toggleStates = new(StringComparer.Ordinal);
    private List<string> _recentColors = RecentColorPalette.Normalize(null).ToList();

    /// <summary>
    /// Gets or sets the state-file schema version. A file with a different version is treated as
    /// stale and regenerated; runtime flags have no migration path by design.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// Gets or sets a value indicating whether the one-time tray notification explaining
    /// close-to-tray behavior has already been shown (RF-006). Persisting it across restarts is
    /// the reason this file exists.
    /// </summary>
    public bool CloseNotificationShown { get; set; }

    /// <summary>
    /// Gets or sets the optional persisted on/off state of Toggle widgets whose config opts in
    /// (<see cref="ToggleWidgetConfig.PersistState"/>, RF-045/M2-RF-452). Keys are the composite
    /// <c>"&lt;pageId&gt;/&lt;widgetId&gt;"</c> value; values are the boolean on/off state. This
    /// map lives only in <c>state.json</c>, never in <c>panel.json</c>. A JSON <c>null</c> is
    /// coalesced to an empty map so a shape mismatch stays non-fatal.
    /// </summary>
    public Dictionary<string, bool> ToggleStates
    {
        get => _toggleStates;
        set => _toggleStates = value ?? new Dictionary<string, bool>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets or sets the most-recently-used colour list shared by the widget appearance pickers and
    /// the page-background picker (user-requested colour picker). Stored only in <c>state.json</c>;
    /// a missing, empty, wholly invalid, or oversized list is normalized tolerantly: malformed
    /// entries are dropped, duplicates are removed case-insensitively, at most
    /// <see cref="RecentColorPalette.Capacity"/> entries are kept, and an empty result falls back
    /// to the standard palette. A JSON <c>null</c> therefore stays non-fatal.
    /// </summary>
    public List<string> RecentColors
    {
        get => _recentColors;
        set => _recentColors = RecentColorPalette.Normalize(value);
    }
}
