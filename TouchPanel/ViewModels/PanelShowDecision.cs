using TouchPanel.Services.Monitors;
using TouchPanel.Services.Notifications;

namespace TouchPanel.ViewModels;

/// <summary>
/// The action the composition root must take for a panel show request (RF-017/RF-024).
/// </summary>
public enum PanelShowOutcome
{
    /// <summary>Place and show the panel on <see cref="PanelShowDecision.Monitor"/> without activation.</summary>
    Show,

    /// <summary>Keep the panel hidden; the request was refused (or the monitor is unavailable).</summary>
    StayHidden,
}

/// <summary>
/// Pure decision returned by <see cref="MainWindowViewModel.RequestShow"/>: what to do with the
/// window, and whether the user must be told (RF-017: fallback and refusal are never silent).
/// The decision carries no WPF types and performs no I/O, so the policy is unit-testable without
/// a UI or hardware.
/// </summary>
public sealed record PanelShowDecision
{
    /// <summary>Gets the outcome: show the panel or keep it hidden.</summary>
    public required PanelShowOutcome Outcome { get; init; }

    /// <summary>Gets the monitor the panel should occupy; <see langword="null"/> when the outcome is <see cref="PanelShowOutcome.StayHidden"/>.</summary>
    public MonitorDescriptor? Monitor { get; init; }

    /// <summary>Gets a value indicating whether <see cref="Monitor"/> is the primary fallback rather than the configured target.</summary>
    public bool UsedFallback { get; init; }

    /// <summary>Gets the notification title, or <see langword="null"/> when no notification is required.</summary>
    public string? NotificationTitle { get; init; }

    /// <summary>Gets the notification body, or <see langword="null"/> when no notification is required.</summary>
    public string? NotificationMessage { get; init; }

    /// <summary>Gets the notification severity; only meaningful when <see cref="ShouldNotify"/> is <see langword="true"/>.</summary>
    public NotificationSeverity NotificationSeverity { get; init; } = NotificationSeverity.Info;

    /// <summary>Gets a value indicating whether the composition root must surface a user notification for this decision.</summary>
    public bool ShouldNotify => NotificationMessage is not null;
}
