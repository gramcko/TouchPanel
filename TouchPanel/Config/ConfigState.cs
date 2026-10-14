namespace TouchPanel.Config;

/// <summary>
/// The four distinguishable configuration load states from RF-075. A load attempt is always
/// classified as exactly one of these so the caller can select the matching handling path
/// (load normally, migrate, recover, or refuse to interpret) without re-inspecting the file.
/// </summary>
public enum ConfigState
{
    /// <summary>
    /// Schema version equals the supported version and validation passed (RF-075.1): load normally.
    /// </summary>
    Current,

    /// <summary>
    /// Schema version is older than supported (RF-075.2): migrate, validate the result, back up the
    /// original, and save the migrated file.
    /// </summary>
    OlderSupported,

    /// <summary>
    /// Unparsable or structurally invalid JSON (RF-075.3): preserve the source, recover from the
    /// last known valid backup or generated defaults, log, and notify.
    /// </summary>
    Invalid,

    /// <summary>
    /// Schema version is newer than supported (RF-075.4): do not interpret or overwrite; recover
    /// from the last known valid backup or generated defaults, log, and notify.
    /// </summary>
    UnsupportedFuture,
}
