namespace TouchPanel.Monitoring;

/// <summary>
/// One selectable sensor in the edit-mode sensor picker (M5-RF-510/M5-RF-511): the stable
/// application-level key written into the widget configuration and a human-readable display name.
/// A plain data record so it stays WPF-free and bindable through the property editor.
/// </summary>
/// <param name="Key">The stable sensor key (for example <c>cpu.total</c> or <c>cpu.core.3</c>).</param>
/// <param name="DisplayName">The touch-friendly label shown in the picker.</param>
public sealed record SensorOption(string Key, string DisplayName);
