using TouchPanel.Services.Monitors;
using TouchPanel.Services.Notifications;

namespace TouchPanel.ViewModels;

/// <summary>
/// The window action required after a display-configuration change (RF-017/RF-022). The policy is
/// pure: <see cref="MainWindowViewModel.ApplyDisplayChange"/> classifies the change and this record
/// carries what the composition root must execute, including any mandatory notification.
/// </summary>
public enum PanelDisplayChangeOutcome
{
    /// <summary>Nothing relevant changed; no window or notification action is required.</summary>
    None,

    /// <summary>Place the panel on <see cref="PanelDisplayChangeDecision.Monitor"/> and show it without activation.</summary>
    Show,

    /// <summary>Re-place the (already visible) panel on <see cref="PanelDisplayChangeDecision.Monitor"/> with its fresh physical bounds.</summary>
    Reposition,

    /// <summary>Hide the panel; <see cref="PanelDisplayChangeDecision.ShouldNotify"/> carries the explanation.</summary>
    Hide,
}

/// <summary>
/// Pure decision returned by <see cref="MainWindowViewModel.ApplyDisplayChange"/>: what to do with
/// the panel after the OS reported a display change, and whether the user must be told. Fallback
/// placements are never silent (RF-017); the record carries no WPF types and performs no I/O, so
/// the policy is unit-testable without a UI or hardware (RNF-006).
/// </summary>
public sealed record PanelDisplayChangeDecision
{
    /// <summary>Gets the window action to execute.</summary>
    public required PanelDisplayChangeOutcome Outcome { get; init; }

    /// <summary>Gets the monitor the panel should occupy for <see cref="PanelDisplayChangeOutcome.Show"/> and <see cref="PanelDisplayChangeOutcome.Reposition"/>; otherwise <see langword="null"/>.</summary>
    public MonitorDescriptor? Monitor { get; init; }

    /// <summary>Gets a value indicating whether <see cref="Monitor"/> is the primary fallback rather than the configured target (RF-017).</summary>
    public bool UsedFallback { get; init; }

    /// <summary>Gets the human-readable reason for the decision, suitable for logs.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Gets the notification title, or <see langword="null"/> when no notification is required.</summary>
    public string? NotificationTitle { get; init; }

    /// <summary>Gets the notification body, or <see langword="null"/> when no notification is required.</summary>
    public string? NotificationMessage { get; init; }

    /// <summary>Gets the notification severity; only meaningful when <see cref="ShouldNotify"/> is <see langword="true"/>.</summary>
    public NotificationSeverity NotificationSeverity { get; init; } = NotificationSeverity.Info;

    /// <summary>Gets a value indicating whether the composition root must surface a user notification for this decision.</summary>
    public bool ShouldNotify => NotificationMessage is not null;
}
