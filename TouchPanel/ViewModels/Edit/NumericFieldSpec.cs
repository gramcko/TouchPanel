using System.ComponentModel;
using System.Globalization;

namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// Immutable description of one numeric property-editor field: the increment used by its steppers,
/// the inclusive valid range, the number of decimal places its canonical text uses, whether a
/// leading minus sign is legal, and whether an empty field means "inherit" (font size and opacity)
/// rather than "invalid".
/// </summary>
/// <remarks>
/// The type is WPF-free so the step/clamp/format rules are unit-testable without a window. The
/// property editor supplies a fresh instance per request because a few bounds depend on live state
/// (geometry is bounded by the current page and widget extents; a range bound is bounded by the
/// opposite bound and the slider step).
/// </remarks>
/// <param name="Step">The normal increment; ignored when <paramref name="StepByMagnitude"/> is set.</param>
/// <param name="Minimum">The inclusive lower bound.</param>
/// <param name="Maximum">The inclusive upper bound.</param>
/// <param name="Decimals">The number of decimal places in the canonical text (0 = whole numbers).</param>
/// <param name="AllowNegative">Whether the field accepts a leading minus sign.</param>
/// <param name="AllowsEmpty">Whether an empty field is a valid "inherit the theme default" state.</param>
/// <param name="StepByMagnitude">
/// When <see langword="true"/>, the step is chosen from the current magnitude with
/// <see cref="NumericFieldMath.MagnitudeStep"/> instead of <paramref name="Step"/>.
/// </param>
/// <param name="FineStep">
/// The step used for magnitude-based fields while the value is below one (default <c>1</c>); the
/// slider step field uses <c>0.1</c> so a fractional step can be tuned without a huge jump.
/// </param>
public sealed record NumericFieldSpec(
    double Step,
    double Minimum,
    double Maximum,
    int Decimals,
    bool AllowNegative,
    bool AllowsEmpty = false,
    bool StepByMagnitude = false,
    double FineStep = 1)
{
    /// <summary>Gets the increment for <paramref name="value"/> under this spec.</summary>
    /// <param name="value">The current field value.</param>
    /// <returns>The increment to apply.</returns>
    public double StepFor(double value)
    {
        if (!StepByMagnitude)
        {
            return Step;
        }

        return Math.Abs(value) < 1 ? FineStep : NumericFieldMath.MagnitudeStep(value);
    }
}

/// <summary>
/// WPF-free numeric-field helpers shared by the property editor and its reusable input controls:
/// formatting, invariant parsing (a typed comma is accepted as the decimal separator), clamping and
/// rounding, the magnitude-based step used by the range bounds, and the step operation itself.
/// </summary>
public static class NumericFieldMath
{
    /// <summary>
    /// Chooses a power-of-ten step that is roughly 1% of <paramref name="value"/> once its magnitude
    /// reaches three digits: <c>1</c> while <c>|value| &lt; 100</c>, otherwise
    /// <c>10^round(log10(|value|) - 2)</c> with a floor of <c>1</c>. Examples: 99 → 1, 100 → 1,
    /// 1,000 → 10, 12,500,000 → 100,000. This is the documented rule for the Minimum/Maximum
    /// steppers so a large range (for example a byte-rate scale) moves in useful jumps.
    /// </summary>
    /// <param name="value">The current value.</param>
    /// <returns>The step to apply.</returns>
    public static double MagnitudeStep(double value)
    {
        var magnitude = Math.Abs(value);
        if (!double.IsFinite(magnitude) || magnitude < 100)
        {
            return 1;
        }

        var exponent = (int)Math.Round(Math.Log10(magnitude) - 2, MidpointRounding.AwayFromZero);
        return Math.Max(1, Math.Pow(10, exponent));
    }

    /// <summary>
    /// Rounds a value to the spec's decimals (away from zero, so 0.95 with one decimal is 1.0) and
    /// clamps it back into the inclusive range. The result is what the field formats and applies, so
    /// the previewed value and the committed text never disagree.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <param name="spec">The field spec.</param>
    /// <returns>The normalized value.</returns>
    public static double Normalize(double value, NumericFieldSpec spec)
    {
        if (!double.IsFinite(value))
        {
            return spec.Minimum;
        }

        var clamped = Math.Clamp(value, spec.Minimum, spec.Maximum);
        var rounded = Math.Round(clamped, Math.Max(0, spec.Decimals), MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, spec.Minimum, spec.Maximum);
    }

    /// <summary>
    /// Formats a value as the field's canonical text: <c>0</c> for whole-number fields, one fixed
    /// decimal for one-decimal fields (opacity), and up to the allowed decimals with trailing zeros
    /// trimmed otherwise. Always uses the invariant culture so the canonical text is stable.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="decimals">The number of allowed decimal places.</param>
    /// <returns>The canonical text.</returns>
    public static string Format(double value, int decimals)
    {
        if (!double.IsFinite(value))
        {
            value = 0;
        }

        var rounded = Math.Round(value, Math.Max(0, decimals), MidpointRounding.AwayFromZero);
        if (decimals <= 0)
        {
            return rounded.ToString("0", CultureInfo.InvariantCulture);
        }

        var format = decimals == 1 ? "0.0" : "0." + new string('#', decimals);
        return rounded.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses field text as a number. A comma is treated as the decimal separator (a touch keyboard
    /// may produce it under a comma-decimal locale) and thousands separators are rejected, so the
    /// parse mirrors what the numeric input filter allows.
    /// </summary>
    /// <param name="text">The candidate text.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true"/> when the text is a finite number.</returns>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var normalized = trimmed.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    /// <summary>
    /// Computes one stepper step from <paramref name="current"/>: the value is first normalized into
    /// the range, moved by the spec's step (or magnitude step), then normalized again. Returns
    /// <see langword="false"/> when the result equals the current position, which is how a bound
    /// disables the corresponding button.
    /// </summary>
    /// <param name="current">The value to step from (already parsed, possibly out of range).</param>
    /// <param name="direction"><c>-1</c> for decrease, <c>+1</c> for increase.</param>
    /// <param name="spec">The field spec.</param>
    /// <param name="next">The normalized next value when a step is possible.</param>
    /// <returns><see langword="true"/> when the value moved.</returns>
    public static bool TryStep(double current, int direction, NumericFieldSpec spec, out double next)
    {
        next = Normalize(current, spec);
        if (direction is not (-1 or 1))
        {
            return false;
        }

        var step = spec.StepFor(next);
        if (!double.IsFinite(step) || step <= 0)
        {
            return false;
        }

        var candidate = Normalize(next + (direction * step), spec);
        if (candidate.Equals(next))
        {
            return false;
        }

        next = candidate;
        return true;
    }
}

/// <summary>
/// Pure numeric-text grammar used by the property editor's text fields (typing and paste). It
/// accepts digits, at most one decimal separator when the field allows decimals (a dot or a comma),
/// and a single leading minus only when the field allows negatives; everything else is rejected so
/// the bound value is never disturbed by letters, symbols, extra separators, or extra signs.
/// </summary>
public static class NumericTextFilter
{
    /// <summary>
    /// Returns whether <paramref name="text"/> is an acceptable (possibly still incomplete, for
    /// example "-" or "1.") numeric entry for the given precision.
    /// </summary>
    /// <param name="text">The candidate text.</param>
    /// <param name="decimals">The allowed decimal places; 0 rejects any separator.</param>
    /// <param name="allowNegative">Whether a leading minus is legal.</param>
    /// <returns><see langword="true"/> when the text is acceptable.</returns>
    public static bool IsValidText(string? text, int decimals, bool allowNegative)
    {
        var candidate = text ?? string.Empty;
        if (candidate.Length == 0)
        {
            return true;
        }

        var index = 0;
        if (candidate[0] == '-')
        {
            if (!allowNegative)
            {
                return false;
            }

            index = 1;
            if (candidate.Length == 1)
            {
                return true;
            }
        }
        else if (candidate[0] == '+')
        {
            return false;
        }

        var seenSeparator = false;
        var fractionDigits = 0;
        for (; index < candidate.Length; index++)
        {
            var character = candidate[index];
            if (char.IsAsciiDigit(character))
            {
                if (seenSeparator)
                {
                    fractionDigits++;
                    if (fractionDigits > decimals)
                    {
                        return false;
                    }
                }

                continue;
            }

            if (character is '.' or ',' && decimals > 0)
            {
                if (seenSeparator)
                {
                    return false;
                }

                seenSeparator = true;
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds the text that would result from replacing the current selection with
    /// <paramref name="inserted"/> and validates it. Selection indices are clamped defensively so a
    /// hostile or stale value never throws.
    /// </summary>
    /// <param name="current">The current field text.</param>
    /// <param name="selectionStart">The selection start (caret position when the length is 0).</param>
    /// <param name="selectionLength">The selected character count.</param>
    /// <param name="inserted">The text being typed or pasted.</param>
    /// <param name="decimals">The allowed decimal places.</param>
    /// <param name="allowNegative">Whether a leading minus is legal.</param>
    /// <param name="candidate">The resulting text when accepted.</param>
    /// <returns><see langword="true"/> when the edit is acceptable.</returns>
    public static bool TryApplyEdit(
        string? current,
        int selectionStart,
        int selectionLength,
        string? inserted,
        int decimals,
        bool allowNegative,
        out string candidate)
    {
        var text = current ?? string.Empty;
        var replacement = inserted ?? string.Empty;
        var start = Math.Clamp(selectionStart, 0, text.Length);
        var length = Math.Clamp(selectionLength, 0, text.Length - start);
        candidate = text[..start] + replacement + text[(start + length)..];
        return IsValidText(candidate, decimals, allowNegative);
    }
}

/// <summary>
/// Deterministic press-and-hold state machine for a numeric stepper: the first repeat becomes due
/// after <see cref="HoldDelay"/> (400 ms) and following repeats every <see cref="RepeatInterval"/>
/// (80 ms) until stopped. It holds no timer itself, so the cadence and the stop conditions are unit
/// testable with virtual time; the WPF button drives it from a <c>DispatcherTimer</c>.
/// </summary>
public sealed class NumericRepeatController
{
    /// <summary>The delay before the first auto-repeat while a stepper is held.</summary>
    public static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>The interval between auto-repeats after the hold delay.</summary>
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(80);

    private bool _holding;
    private double _elapsedMilliseconds;
    private double _nextRepeatMilliseconds;

    /// <summary>Gets a value indicating whether a press is currently held.</summary>
    public bool IsHolding => _holding;

    /// <summary>
    /// Gets the delay until the next repeat, or <see langword="null"/> when no press is held. The
    /// button uses it as the next <c>DispatcherTimer</c> interval.
    /// </summary>
    public TimeSpan? NextDelay => _holding
        ? TimeSpan.FromMilliseconds(Math.Max(0, _nextRepeatMilliseconds - _elapsedMilliseconds))
        : null;

    /// <summary>Starts a hold; the first repeat is due after <see cref="HoldDelay"/>.</summary>
    public void Press()
    {
        _holding = true;
        _elapsedMilliseconds = 0;
        _nextRepeatMilliseconds = HoldDelay.TotalMilliseconds;
    }

    /// <summary>Ends a hold; all further advances are inert until the next <see cref="Press"/>.</summary>
    public void Release() => _holding = false;

    /// <summary>
    /// Advances virtual time and reports whether one repeat is due now. At most one repeat is
    /// reported per call (the button calls it once per timer tick); the next due time then advances
    /// by <see cref="RepeatInterval"/>.
    /// </summary>
    /// <param name="elapsed">The time elapsed since the previous call.</param>
    /// <returns><see langword="true"/> when a repeat should be executed.</returns>
    public bool Advance(TimeSpan elapsed)
    {
        if (!_holding)
        {
            return false;
        }

        _elapsedMilliseconds += Math.Max(0, elapsed.TotalMilliseconds);
        if (_elapsedMilliseconds + 0.0001 < _nextRepeatMilliseconds)
        {
            return false;
        }

        _nextRepeatMilliseconds += RepeatInterval.TotalMilliseconds;
        return true;
    }
}

/// <summary>
/// Host contract implemented by the property editor for the reusable numeric input and stepper
/// controls. Keeping it WPF-free lets the controls resolve the field's live spec and text without
/// knowing which widget type is being edited, and lets the editor own the single live-edit session
/// (and therefore the single undo group).
/// </summary>
public interface INumericFieldHost : INotifyPropertyChanged
{
    /// <summary>Gets the current spec for a field, or <see langword="null"/> when the field is not numeric here.</summary>
    /// <param name="field">The field key (the VM property name, for example <c>XText</c>).</param>
    /// <returns>The field spec.</returns>
    NumericFieldSpec? GetNumericFieldSpec(string field);

    /// <summary>Gets the field's current raw text.</summary>
    /// <param name="field">The field key.</param>
    /// <returns>The raw text.</returns>
    string GetNumericFieldText(string field);

    /// <summary>Begins (or continues) the live-edit session for a field.</summary>
    /// <param name="field">The field key.</param>
    /// <param name="fromFocus">Whether keyboard focus opened the session (typing) or a stepper did.</param>
    void BeginNumericLiveEdit(string field, bool fromFocus);

    /// <summary>Commits the session for a field (Enter/LostFocus).</summary>
    /// <param name="field">The field key.</param>
    void CommitNumericLiveEdit(string field);

    /// <summary>Ends a stepper-opened session on release so one hold is one undo group.</summary>
    /// <param name="field">The field key.</param>
    void CommitNumericStepHold(string field);

    /// <summary>Applies one step and rewrites the canonical text.</summary>
    /// <param name="field">The field key.</param>
    /// <param name="direction"><c>-1</c> or <c>+1</c>.</param>
    /// <returns><see langword="true"/> when the value moved.</returns>
    bool StepNumericField(string field, int direction);

    /// <summary>Gets whether a step in the given direction would move the value.</summary>
    /// <param name="field">The field key.</param>
    /// <param name="direction"><c>-1</c> or <c>+1</c>.</param>
    /// <returns><see langword="true"/> when the corresponding button is enabled.</returns>
    bool CanStepNumericField(string field, int direction);
}
