using System.Globalization;

namespace TouchPanel.Services.Updates;

/// <summary>
/// A release-tag version (for example <c>v1.0.2</c> or <c>1.2.3-beta.1</c>). Parsing is deliberately
/// lenient about the optional leading <c>v</c> and a missing patch component because GitHub release
/// tags are authored by hand, while comparison follows semantic-version precedence for the numeric
/// core. Build metadata (<c>+abc</c>) is ignored.
/// </summary>
/// <param name="Major">The major component.</param>
/// <param name="Minor">The minor component.</param>
/// <param name="Patch">The patch component.</param>
/// <param name="PreRelease">The optional pre-release label without the leading hyphen.</param>
public readonly record struct VersionTag(int Major, int Minor, int Patch, string? PreRelease = null)
    : IComparable<VersionTag>
{
    /// <summary>
    /// Parses a tag into a <see cref="VersionTag"/>. Accepts an optional leading <c>v</c>/<c>V</c>,
    /// one to three numeric components, and an optional pre-release suffix.
    /// </summary>
    /// <param name="tag">The raw tag, for example <c>v1.0.2</c>; may be <see langword="null"/>.</param>
    /// <param name="version">The parsed version; default when parsing fails.</param>
    /// <returns><see langword="true"/> when the tag is a valid version.</returns>
    public static bool TryParse(string? tag, out VersionTag version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var text = tag.Trim();

        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];

            if (text.Length == 0)
            {
                return false;
            }
        }

        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }

        string? preRelease = null;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = text[(dash + 1)..];
            text = text[..dash];

            if (preRelease.Length == 0)
            {
                return false;
            }
        }

        var parts = text.Split('.');
        if (parts.Length is 0 or > 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length == 0 ||
                !int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
            {
                return false;
            }
        }

        version = new VersionTag(numbers[0], numbers[1], numbers[2], preRelease);
        return true;
    }

    /// <summary>
    /// Parses a tag, returning <see langword="null"/> when it is not a valid version.
    /// </summary>
    /// <param name="tag">The raw tag.</param>
    /// <returns>The parsed version or <see langword="null"/>.</returns>
    public static VersionTag? ParseOrNull(string? tag) => TryParse(tag, out var version) ? version : null;

    /// <inheritdoc />
    public int CompareTo(VersionTag other)
    {
        var core = Major.CompareTo(other.Major);
        if (core != 0)
        {
            return core;
        }

        core = Minor.CompareTo(other.Minor);
        if (core != 0)
        {
            return core;
        }

        core = Patch.CompareTo(other.Patch);
        if (core != 0)
        {
            return core;
        }

        // A final release outranks any pre-release of the same numeric version (semver precedence).
        if (PreRelease is null && other.PreRelease is null)
        {
            return 0;
        }

        if (PreRelease is null)
        {
            return 1;
        }

        if (other.PreRelease is null)
        {
            return -1;
        }

        return string.Compare(PreRelease, other.PreRelease, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the canonical form without the leading <c>v</c>, for example <c>1.0.2</c>.</summary>
    /// <returns>The normalized version text.</returns>
    public override string ToString() =>
        PreRelease is null
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{PreRelease}");
}
