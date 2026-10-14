using System.Globalization;
using TouchPanel.Config;

namespace TouchPanel.Services.Time;

/// <summary>
/// Pure, WPF-free formatting rules for the Clock widget (RF-046/M3-RF-462/463): it maps a
/// <see cref="ClockMode"/> preset to a format string, validates a bounded custom format through a
/// safe trial format, and detects whether a format contains a real seconds/sub-second specifier
/// (escaped-literal-aware) so the view model can schedule the correct refresh boundary.
/// </summary>
/// <remarks>
/// The detection is deliberately conservative: only unescaped, unquoted <c>s</c>/<c>f</c>/<c>F</c>
/// specifiers force a per-second refresh. The <c>t</c>/<c>tt</c> AM/PM designators do not, so a
/// 12-hour format without a seconds field still refreshes on the minute boundary (M3-RF-463).
/// </remarks>
public static class ClockFormat
{
    /// <summary>The preset used when a mode is unrecognized (M3-RF-462 default).</summary>
    public const ClockMode DefaultMode = ClockMode.Time24;

    /// <summary>Returns the custom format string for a preset mode.</summary>
    /// <param name="mode">The configured preset; an unknown sentinel maps to <see cref="DefaultMode"/>.</param>
    /// <returns>A culture-aware standard or custom format string.</returns>
    public static string PresetFormat(ClockMode mode) => mode switch
    {
        ClockMode.Time12 => "h:mm tt",
        ClockMode.DateShort => "d",
        ClockMode.DateLong => "D",
        ClockMode.DateTime => "g",
        _ => "HH:mm",
    };

    /// <summary>
    /// Returns a value indicating whether a custom format is usable. The custom format must be
    /// non-blank, within
    /// <see cref="ClockWidgetConfig.MinFormatLength"/>..<see cref="ClockWidgetConfig.MaxFormatLength"/>
    /// characters, and format without throwing in the supplied culture (M3-RF-462).
    /// </summary>
    /// <param name="customFormat">The optional custom format, or <see langword="null"/>.</param>
    /// <param name="culture">The pinned display culture.</param>
    /// <returns><see langword="true"/> when the custom format is usable.</returns>
    public static bool TryCustomFormat(string? customFormat, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(customFormat)
            || customFormat.Length < ClockWidgetConfig.MinFormatLength
            || customFormat.Length > ClockWidgetConfig.MaxFormatLength)
        {
            return false;
        }

        return IsFormatSafe(customFormat, culture);
    }

    /// <summary>
    /// Returns a value indicating whether a format string can be applied to a sample instant in the
    /// supplied culture without throwing. Used by both runtime resolution and load-time validation.
    /// </summary>
    /// <param name="format">The candidate format string.</param>
    /// <param name="culture">The culture the format is evaluated in.</param>
    /// <returns><see langword="true"/> when the format is usable.</returns>
    public static bool IsFormatSafe(string format, CultureInfo culture)
    {
        try
        {
            _ = DateTimeOffset.UnixEpoch.ToString(format, culture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns a value indicating whether the format contains a real seconds (<c>s</c>) or
    /// sub-second (<c>f</c>/<c>F</c>) specifier that requires a one-second refresh. Escaped
    /// (<c>\</c>) and quoted (<c>'</c>/<c>"</c>) literals are ignored (M3-RF-463).
    /// </summary>
    /// <param name="format">The effective format string.</param>
    /// <returns><see langword="true"/> when a per-second refresh is required.</returns>
    public static bool RequiresPerSecondRefresh(string format)
    {
        ArgumentNullException.ThrowIfNull(format);

        var quote = '\0';
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];

            if (c == '\\')
            {
                // A backslash escapes exactly the next character.
                i++;
                continue;
            }

            if (quote != '\0')
            {
                // Inside a quoted literal nothing is a specifier; a doubled quote is an escape.
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                continue;
            }

            if (c is 's' or 'f' or 'F')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes the delay until the next refresh boundary: the next whole second for a per-second
    /// format, or the next whole minute otherwise (M3-RF-463). The result is always strictly
    /// positive so a reschedule can never spin.
    /// </summary>
    /// <param name="now">The current local instant.</param>
    /// <param name="perSecond">Whether the effective format needs per-second updates.</param>
    /// <returns>The delay until the next boundary (1–1000 ms or 1–60 000 ms).</returns>
    public static TimeSpan ComputeNextBoundary(DateTimeOffset now, bool perSecond)
    {
        var millisecondsIntoPeriod = perSecond
            ? now.Millisecond
            : (now.Second * 1000) + now.Millisecond;

        var periodMilliseconds = perSecond ? 1000 : 60_000;
        return TimeSpan.FromMilliseconds(periodMilliseconds - millisecondsIntoPeriod);
    }
}
