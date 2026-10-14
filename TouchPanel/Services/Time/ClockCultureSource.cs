using System.Globalization;

namespace TouchPanel.Services.Time;

/// <summary>
/// Default, WPF-free implementation of <see cref="IClockCultureSource"/> (M4-RF-951/D-M4-5). The
/// composition root (<c>App.xaml.cs</c>) captures the user's current culture once into an instance
/// and supplies that same instance to both the configuration validator's load path and every Clock
/// widget, so validation and display cannot diverge. <see cref="Default"/> preserves the documented
/// behavior when no instance is injected (tests and legacy callers): the process's current culture
/// captured once at first use.
/// </summary>
public sealed class ClockCultureSource : IClockCultureSource
{
    /// <summary>
    /// The process-wide default source, captured once from <see cref="CultureInfo.CurrentCulture"/>.
    /// Used when no explicit source is injected, keeping the validator's fallback aligned with the
    /// Clock view model's fallback.
    /// </summary>
    public static ClockCultureSource Default { get; } = new(CultureInfo.CurrentCulture);

    /// <summary>Initializes a new instance of the <see cref="ClockCultureSource"/> class.</summary>
    /// <param name="culture">The pinned display culture; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is <see langword="null"/>.</exception>
    public ClockCultureSource(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        Culture = culture;
    }

    /// <inheritdoc/>
    public CultureInfo Culture { get; }
}
