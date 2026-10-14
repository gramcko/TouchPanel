using System.Text;
using System.Text.RegularExpressions;

namespace TouchPanel.Services.Logging;

/// <summary>
/// The single place sensitive values are scrubbed before they reach a log (RF-051, RF-085).
/// Pure and deterministic so it can be reused by the action layer (T-09) and covered by
/// unit tests. A value is masked when its key looks sensitive; free text is additionally
/// scanned for <c>key=value</c>/<c>key: value</c> pairs, JSON fields and bearer/basic tokens.
/// </summary>
public static partial class LogRedactor
{
    /// <summary>Replacement written instead of a sensitive value.</summary>
    public const string Mask = "***";

    private static readonly HashSet<string> SensitiveKeyNames = new(StringComparer.Ordinal)
    {
        "key",
        "apikey",
        "auth",
        "authorization",
        "password",
        "passwd",
        "pwd",
        "secret",
        "token",
        "credential",
        "credentials",
        "privatekey",
        "accesskey",
        "secretkey",
        "clientsecret",
        "connectionstring",
        "bearertoken",
    };

    private static readonly string[] SensitiveKeyFragments =
    {
        "password",
        "passwd",
        "secret",
        "token",
        "credential",
        "apikey",
        "privatekey",
        "accesskey",
        "authorization",
        "connectionstring",
        "clientsecret",
    };

    /// <summary>
    /// Determines whether a parameter/key name denotes a sensitive value. Matching is
    /// case-insensitive and ignores separators (<c>api_key</c>, <c>X-Api-Key</c> and
    /// <c>apiKey</c> are equivalent). Bare <c>key</c> only matches the exact name so
    /// words such as <c>monkey</c> or <c>keyboard</c> are not treated as secrets.
    /// </summary>
    /// <param name="key">The parameter/key name to classify.</param>
    /// <returns><see langword="true"/> when the value for this key must be masked.</returns>
    public static bool IsSensitiveKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var normalized = NormalizeKey(key);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (SensitiveKeyNames.Contains(normalized))
        {
            return true;
        }

        foreach (var fragment in SensitiveKeyFragments)
        {
            if (normalized.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see cref="Mask"/> when the key is sensitive and the value is non-empty;
    /// otherwise returns the value unchanged.
    /// </summary>
    /// <param name="key">The parameter/key name.</param>
    /// <param name="value">The value that may need masking.</param>
    /// <returns>The mask for sensitive values, the original value otherwise.</returns>
    public static string? RedactValue(string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return IsSensitiveKey(key) && !string.IsNullOrEmpty(value) ? Mask : value;
    }

    /// <summary>
    /// Redacts a set of parameters: sensitive keys are replaced with <see cref="Mask"/>,
    /// and non-sensitive values are passed through <see cref="RedactText"/> so secrets
    /// embedded in free-form values (for example in a URL or header string) are scrubbed too.
    /// </summary>
    /// <param name="parameters">The parameter set to redact.</param>
    /// <returns>A new dictionary safe to log.</returns>
    public static IReadOnlyDictionary<string, string?> RedactParameters(
        IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var redacted = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (IsSensitiveKey(parameter.Key))
            {
                redacted[parameter.Key] = RedactValue(parameter.Key, parameter.Value);
            }
            else
            {
                redacted[parameter.Key] = parameter.Value is null ? null : RedactText(parameter.Value);
            }
        }

        return redacted;
    }

    /// <summary>
    /// Scrubs sensitive values embedded in free text: bearer/basic tokens, unquoted
    /// <c>key=value</c>/<c>key: value</c> pairs and JSON string fields whose name looks
    /// sensitive. On hostile input that exhausts the regex budget the whole text is
    /// masked (fail closed) rather than risking a leaked secret.
    /// </summary>
    /// <param name="text">The text to scrub; <see langword="null"/> yields <see cref="string.Empty"/>.</param>
    /// <returns>The scrubbed text.</returns>
    public static string RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        try
        {
            var redacted = BearerTokenRegex().Replace(text, m => m.Groups["scheme"].Value + " " + Mask);
            redacted = SensitiveAssignmentRegex().Replace(redacted, m => m.Groups["key"].Value + "=" + Mask);
            redacted = SensitiveJsonRegex().Replace(redacted, m => m.Groups["key"].Value + ": \"" + Mask + "\"");
            return redacted;
        }
        catch (RegexMatchTimeoutException)
        {
            return Mask;
        }
    }

    private static string NormalizeKey(string key)
    {
        var builder = new StringBuilder(key.Length);
        foreach (var character in key)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(
        @"(?<scheme>bearer|basic)\s+(?<token>[A-Za-z0-9\-._~+/=]{8,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        250)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(
        @"(?<key>[A-Za-z0-9_.\-]*(?:password|passwd|pwd|secret|token|credential|authorization|auth|api[_-]?key|private[_-]?key|access[_-]?key)[A-Za-z0-9_.\-]*)\s*[:=]\s*(?<value>""[^""]*""|'[^']*'|[^\s,;&]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        250)]
    private static partial Regex SensitiveAssignmentRegex();

    [GeneratedRegex(
        @"(?<key>""[^""]*(?:password|passwd|pwd|secret|token|credential|authorization|auth|api[_-]?key|private[_-]?key|access[_-]?key)[^""]*"")\s*:\s*(?<value>""(?:[^""\\]|\\.)*"")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        250)]
    private static partial Regex SensitiveJsonRegex();
}
