using TouchPanel.Config;

namespace TouchPanel.Services.Monitors;

/// <summary>
/// Which identity level produced a match, in RF-016 priority order.
/// </summary>
public enum MonitorMatchCriterion
{
    /// <summary>No match (the result carries a reason instead).</summary>
    None = 0,

    /// <summary>Matched by EDID-derived hardware identity (manufacturer + product code [+ serial]).</summary>
    HardwareId,

    /// <summary>Matched by display device path or monitor device instance identifier.</summary>
    DevicePath,

    /// <summary>Matched by friendly name.</summary>
    FriendlyName,
}

/// <summary>
/// Outcome of <see cref="MonitorMatcher.Match"/>: the resolved descriptor (or <see langword="null"/>
/// when nothing matched), the criterion that resolved it, and a human-readable reason for logs
/// (RF-016). A no-match result always carries a reason, never a descriptor.
/// </summary>
public sealed record MonitorMatchResult
{
    private MonitorMatchResult(MonitorDescriptor? monitor, MonitorMatchCriterion criterion, string reason)
    {
        Monitor = monitor;
        Criterion = criterion;
        Reason = reason;
    }

    /// <summary>Gets the resolved monitor, or <see langword="null"/> when the target is unavailable.</summary>
    public MonitorDescriptor? Monitor { get; }

    /// <summary>Gets the criterion that produced the match, or <see cref="MonitorMatchCriterion.None"/> on no match.</summary>
    public MonitorMatchCriterion Criterion { get; }

    /// <summary>Gets a human-readable explanation of the outcome, suitable for structured logs.</summary>
    public string Reason { get; }

    /// <summary>Gets a value indicating whether a monitor was resolved.</summary>
    public bool IsMatch => Monitor is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="monitor">The resolved monitor.</param>
    /// <param name="criterion">The criterion that resolved it.</param>
    /// <param name="reason">A human-readable explanation.</param>
    /// <returns>The match result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="monitor"/> is <see langword="null"/>.</exception>
    public static MonitorMatchResult Match(MonitorDescriptor monitor, MonitorMatchCriterion criterion, string reason)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return new MonitorMatchResult(monitor, criterion, reason);
    }

    /// <summary>Creates a no-match result with the reason the target is considered unavailable.</summary>
    /// <param name="reason">A human-readable explanation; must not be empty.</param>
    /// <returns>The no-match result.</returns>
    public static MonitorMatchResult NoMatch(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new MonitorMatchResult(null, MonitorMatchCriterion.None, reason);
    }
}

/// <summary>
/// Pure, deterministic monitor matcher (RF-016, DD-4): resolves a configured
/// <see cref="MonitorIdentity"/> against the discovered <see cref="MonitorDescriptor"/> values
/// without any dependency on Win32, WPF, or hardware (RNF-006). Never relies on a monitor index.
/// </summary>
/// <remarks>
/// <para><b>Priority chain (RF-016).</b> Configured criteria are applied in order:
/// hardware identity, then device path / device instance identifier, then friendly name. A level is
/// skipped only when no candidate exposes that field (for example EDID data missing); a level that
/// is applicable but matches none of the candidates stops the search with a no-match, so the panel
/// is never placed on an unexpected monitor (RF-017). A level that narrows the candidates to
/// exactly one resolves the target immediately.</para>
/// <para><b>Hardware identity.</b> A configured <c>hardwareId</c> may be either the model identity
/// (<c>DEL-A0BC</c>) or the unit identity with the best-effort serial token
/// (<c>DEL-A0BC-4353</c>); both are compared case-insensitively and trimmed.</para>
/// <para><b>Friendly name and resolution.</b> Schema v1 stores no resolution in the identity block,
/// so resolution cannot act as a filter; the physical bounds (resolution and position) are logged at
/// discovery time (D-17) for the user to pin a different criterion. Duplicates that share a friendly
/// name are resolved by the tiebreaks below.</para>
/// <para><b>Ambiguous matches (RF-016).</b> When every configured level has been applied and more
/// than one candidate remains (for example two identical monitors with no serial), the result is
/// resolved deterministically and the choice is logged: (1) a unique primary monitor wins;
/// (2) otherwise the left-most position (smallest X), then the top-most position (smallest Y);
/// (3) otherwise the stable enumeration order, which <see cref="IMonitorEnumerator"/> guarantees to
/// be sorted.</para>
/// </remarks>
public static class MonitorMatcher
{
    /// <summary>
    /// Matches <paramref name="identity"/> against <paramref name="monitors"/> using the RF-016
    /// priority chain and deterministic tiebreaks (see the class remarks).
    /// </summary>
    /// <param name="identity">
    /// The configured identity block, or <see langword="null"/> when no criteria are configured
    /// (first run, D-17); in that case the result is a no-match with an explanatory reason and the
    /// caller applies its fallback policy.
    /// </param>
    /// <param name="monitors">The discovered monitors; may be empty.</param>
    /// <returns>
    /// The resolved monitor plus criterion and reason, or a no-match result carrying the reason the
    /// target is unavailable. Never <see langword="null"/>.
    /// </returns>
    public static MonitorMatchResult Match(MonitorIdentity? identity, IReadOnlyList<MonitorDescriptor>? monitors)
    {
        monitors ??= [];

        if (identity is null || !HasAnyCriterion(identity))
        {
            return MonitorMatchResult.NoMatch("no monitor identity criteria are configured");
        }

        if (monitors.Count == 0)
        {
            return MonitorMatchResult.NoMatch("no monitors were detected");
        }

        var candidates = monitors.ToList();
        var applied = new List<string>(3);
        var skipped = new List<string>(3);

        // RF-016 priority 1: EDID-derived hardware identity (manufacturer + product code + serial).
        if (IsConfigured(identity.HardwareId))
        {
            if (!candidates.Any(HasHardwareIdentity))
            {
                skipped.Add("hardwareId");
            }
            else
            {
                var filtered = candidates.Where(monitor => MatchesHardwareId(monitor, identity.HardwareId!)).ToList();
                if (filtered.Count == 0)
                {
                    return MonitorMatchResult.NoMatch(
                        DescribeMismatch("hardwareId", identity.HardwareId!, candidates.Count, skipped));
                }

                candidates = filtered;
                applied.Add("hardwareId");
                if (candidates.Count == 1)
                {
                    return Unique(candidates[0], MonitorMatchCriterion.HardwareId, "hardwareId", identity.HardwareId!, applied, skipped);
                }
            }
        }

        // RF-016 priority 2: display device path / device instance identifier.
        if (IsConfigured(identity.DevicePath))
        {
            if (!candidates.Any(HasDevicePath))
            {
                skipped.Add("devicePath");
            }
            else
            {
                var filtered = candidates.Where(monitor => MatchesDevicePath(monitor, identity.DevicePath!)).ToList();
                if (filtered.Count == 0)
                {
                    return MonitorMatchResult.NoMatch(
                        DescribeMismatch("devicePath", identity.DevicePath!, candidates.Count, skipped));
                }

                candidates = filtered;
                applied.Add("devicePath");
                if (candidates.Count == 1)
                {
                    return Unique(candidates[0], MonitorMatchCriterion.DevicePath, "devicePath", identity.DevicePath!, applied, skipped);
                }
            }
        }

        // RF-016 priority 3: friendly name; duplicates fall through to the deterministic tiebreak.
        if (IsConfigured(identity.FriendlyName))
        {
            if (!candidates.Any(HasFriendlyName))
            {
                skipped.Add("friendlyName");
            }
            else
            {
                var filtered = candidates.Where(monitor => MatchesFriendlyName(monitor, identity.FriendlyName!)).ToList();
                if (filtered.Count == 0)
                {
                    return MonitorMatchResult.NoMatch(
                        DescribeMismatch("friendlyName", identity.FriendlyName!, candidates.Count, skipped));
                }

                candidates = filtered;
                applied.Add("friendlyName");
                if (candidates.Count == 1)
                {
                    return Unique(candidates[0], MonitorMatchCriterion.FriendlyName, "friendlyName", identity.FriendlyName!, applied, skipped);
                }
            }
        }

        if (applied.Count == 0)
        {
            return MonitorMatchResult.NoMatch(DescribeAllSkipped(skipped));
        }

        return ResolveAmbiguity(candidates, applied, skipped);
    }

    private static MonitorMatchResult Unique(
        MonitorDescriptor monitor,
        MonitorMatchCriterion criterion,
        string criterionName,
        string configuredValue,
        List<string> applied,
        List<string> skipped)
    {
        var reason = applied.Count > 1
            ? $"matched by {criterionName} '{configuredValue}' after narrowing with {string.Join(", ", applied.Take(applied.Count - 1))}"
            : $"matched by {criterionName} '{configuredValue}'";

        if (skipped.Count > 0)
        {
            reason += $"; {string.Join(", ", skipped)} not exposed by the detected monitors";
        }

        return MonitorMatchResult.Match(monitor, criterion, reason);
    }

    private static MonitorMatchResult ResolveAmbiguity(
        List<MonitorDescriptor> candidates,
        List<string> applied,
        List<string> skipped)
    {
        MonitorDescriptor chosen;
        string tiebreak;

        var primaries = candidates.Where(monitor => monitor.IsPrimary).ToList();
        if (primaries.Count == 1)
        {
            chosen = primaries[0];
            tiebreak = "primary-monitor preference";
        }
        else
        {
            var ordered = candidates
                .OrderBy(monitor => monitor.Bounds.X)
                .ThenBy(monitor => monitor.Bounds.Y)
                .ThenBy(StableKey, StringComparer.Ordinal)
                .ToList();

            chosen = ordered[0];
            tiebreak = ordered.Count > 1 && ordered[0].Bounds.X != ordered[1].Bounds.X
                ? $"left-most position (x={chosen.Bounds.X})"
                : ordered.Count > 1 && ordered[0].Bounds.Y != ordered[1].Bounds.Y
                    ? $"top-most position (y={chosen.Bounds.Y})"
                    : "stable enumeration order";
        }

        var criterion = applied[^1] switch
        {
            "hardwareId" => MonitorMatchCriterion.HardwareId,
            "devicePath" => MonitorMatchCriterion.DevicePath,
            _ => MonitorMatchCriterion.FriendlyName,
        };

        var reason = $"ambiguous match: {candidates.Count} monitors matched {string.Join(" + ", applied)}; "
            + $"selected {chosen.DisplayName} ({chosen.Bounds}) by {tiebreak}";

        if (skipped.Count > 0)
        {
            reason += $"; {string.Join(", ", skipped)} not exposed by the detected monitors";
        }

        return MonitorMatchResult.Match(chosen, criterion, reason);
    }

    private static string DescribeMismatch(string criterionName, string value, int candidateCount, List<string> skipped)
    {
        var reason = $"configured {criterionName} '{value}' matched none of the {candidateCount} candidate monitor(s)";

        if (skipped.Count > 0)
        {
            reason += $" (skipped: {string.Join(", ", skipped)})";
        }

        return reason;
    }

    private static string DescribeAllSkipped(List<string> skipped) =>
        $"none of the configured identity fields ({string.Join(", ", skipped)}) are exposed by the detected monitors";

    private static bool HasAnyCriterion(MonitorIdentity identity) =>
        IsConfigured(identity.HardwareId) || IsConfigured(identity.DevicePath) || IsConfigured(identity.FriendlyName);

    private static bool IsConfigured(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool HasHardwareIdentity(MonitorDescriptor monitor) =>
        IsConfigured(monitor.HardwareId) || IsConfigured(monitor.HardwareIdWithSerial);

    private static bool MatchesHardwareId(MonitorDescriptor monitor, string configured) =>
        EqualsTrimmed(monitor.HardwareId, configured) || EqualsTrimmed(monitor.HardwareIdWithSerial, configured);

    private static bool HasDevicePath(MonitorDescriptor monitor) =>
        IsConfigured(monitor.DevicePath) || IsConfigured(monitor.MonitorDeviceId);

    private static bool MatchesDevicePath(MonitorDescriptor monitor, string configured) =>
        EqualsTrimmed(monitor.DevicePath, configured) || EqualsTrimmed(monitor.MonitorDeviceId, configured);

    private static bool HasFriendlyName(MonitorDescriptor monitor) => IsConfigured(monitor.FriendlyName);

    private static bool MatchesFriendlyName(MonitorDescriptor monitor, string configured) =>
        EqualsTrimmed(monitor.FriendlyName, configured);

    private static bool EqualsTrimmed(string? left, string right) =>
        !string.IsNullOrWhiteSpace(left) && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string StableKey(MonitorDescriptor monitor) =>
        monitor.DevicePath ?? monitor.MonitorDeviceId ?? monitor.SourceGdiName ?? monitor.FriendlyName ?? string.Empty;
}
