using System.Globalization;

namespace TouchPanel.Services.Time;

/// <summary>
/// The single canonical source of the Clock display culture (M4-RF-951/D-M4-5). Both the load-time
/// custom-format validation (<c>ConfigValidator.ValidateClock</c>) and the runtime resolution
/// (<c>ClockWidgetViewModel</c>) resolve through this source so validator and widget always agree,
/// removing the <see cref="CultureInfo.InvariantCulture"/>/display-culture divergence. It is
/// WPF-free and injectable so tests can pin a specific culture.
/// </summary>
public interface IClockCultureSource
{
    /// <summary>Gets the pinned display culture used to validate and render Clock formats.</summary>
    CultureInfo Culture { get; }
}
