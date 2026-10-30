using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace LayerPeek.Core.Updates;

/// <summary>
/// A version of the app as Semantic Versioning 2.0.0 defines it (<c>MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]</c>, T-058), read
/// from a release tag (<c>v0.3.1</c>, T-055) or from the app's own version (<c>0.3.1</c>). Compared by SemVer precedence: the
/// numbers, then a version without pre-release identifiers above one with them, then the identifiers one by one (numeric
/// ones numerically and below alphanumeric ones, alphanumeric ones in ASCII order, fewer below more). Build metadata is
/// kept for display but never compared, so two versions differing only in it are equal.
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    /// <summary>Longest text accepted; a real tag is far shorter, anything longer comes from broken data.</summary>
    public const int MaxLength = 128;

    private readonly string[] _prerelease;

    private ReleaseVersion(int major, int minor, int patch, string[] prerelease, string? build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _prerelease = prerelease;
        Build = build;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The dot-separated pre-release identifiers (<c>beta.2</c> → <c>beta</c>, <c>2</c>); empty for a release.</summary>
    public IReadOnlyList<string> PrereleaseIdentifiers => _prerelease;

    /// <summary>The build metadata after <c>+</c>, or <see langword="null"/>; not part of the precedence.</summary>
    public string? Build { get; }

    /// <summary>Whether the version has a pre-release suffix (<c>1.0.0-rc.1</c>).</summary>
    public bool IsPrerelease => _prerelease.Length > 0;

    /// <summary>
    /// Reads <paramref name="text"/>: an optional <c>v</c> or <c>V</c> prefix (release tags), three numbers without leading
    /// zeros, optional pre-release identifiers and build metadata.
    /// </summary>
    /// <returns><see langword="false"/> for anything else (empty, too long, missing parts, leading zeros, invalid characters).</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out ReleaseVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        ReadOnlySpan<char> rest = text;
        if (rest[0] is 'v' or 'V')
        {
            rest = rest[1..];
        }

        string? build = null;
        int plus = rest.IndexOf('+');
        if (plus >= 0)
        {
            ReadOnlySpan<char> buildPart = rest[(plus + 1)..];
            if (!AreIdentifiers(buildPart, numericRule: false))
            {
                return false;
            }

            build = buildPart.ToString();
            rest = rest[..plus];
        }

        string[] prerelease = [];
        int dash = rest.IndexOf('-');
        if (dash >= 0)
        {
            ReadOnlySpan<char> prereleasePart = rest[(dash + 1)..];
            if (!AreIdentifiers(prereleasePart, numericRule: true))
            {
                return false;
            }

            prerelease = prereleasePart.ToString().Split('.');
            rest = rest[..dash];
        }

        Span<Range> parts = stackalloc Range[4];
        if (rest.Split(parts, '.') != 3
            || !TryNumber(rest[parts[0]], out int major)
            || !TryNumber(rest[parts[1]], out int minor)
            || !TryNumber(rest[parts[2]], out int patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch, prerelease, build);
        return true;
    }

    /// <exception cref="FormatException"><paramref name="text"/> is not a version (<see cref="TryParse"/>).</exception>
    public static ReleaseVersion Parse(string text) =>
        TryParse(text, out ReleaseVersion? version) ? version : throw new FormatException($"'{text}' is not a semantic version.");

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Patch.CompareTo(other.Patch);
        }

        return result != 0 ? result : ComparePrerelease(_prerelease, other._prerelease);
    }

    public bool Equals(ReleaseVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => Equals(obj as ReleaseVersion);

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Major);
        hash.Add(Minor);
        hash.Add(Patch);
        foreach (string identifier in _prerelease)
        {
            hash.Add(identifier, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>The version as SemVer writes it, without the <c>v</c> prefix and without build metadata (<c>0.4.0-beta.1</c>).</summary>
    public override string ToString() =>
        _prerelease.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{string.Join('.', _prerelease)}");

    public static bool operator ==(ReleaseVersion? left, ReleaseVersion? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ReleaseVersion? left, ReleaseVersion? right) => !(left == right);

    public static bool operator <(ReleaseVersion? left, ReleaseVersion? right) => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(ReleaseVersion? left, ReleaseVersion? right) => left is null || left.CompareTo(right) <= 0;

    public static bool operator >(ReleaseVersion? left, ReleaseVersion? right) => left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(ReleaseVersion? left, ReleaseVersion? right) => left is null ? right is null : left.CompareTo(right) >= 0;

    private static int ComparePrerelease(string[] left, string[] right)
    {
        // A release ranks above every pre-release of the same numbers.
        if (left.Length == 0 || right.Length == 0)
        {
            return right.Length.CompareTo(left.Length);
        }

        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            int result = CompareIdentifier(left[i], right[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        bool leftNumeric = IsDigits(left);
        bool rightNumeric = IsDigits(right);
        if (leftNumeric && rightNumeric)
        {
            // No leading zeros (checked when parsing): the longer number is the larger one, whatever its size.
            int byLength = left.Length.CompareTo(right.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
        }

        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return Math.Sign(string.CompareOrdinal(left, right));
    }

    /// <summary>Dot-separated, non-empty identifiers of ASCII letters, digits and hyphens; numeric pre-release ones without leading zeros.</summary>
    private static bool AreIdentifiers(ReadOnlySpan<char> text, bool numericRule)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        foreach (Range range in text.Split('.'))
        {
            ReadOnlySpan<char> identifier = text[range];
            if (identifier.IsEmpty)
            {
                return false;
            }

            foreach (char c in identifier)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                {
                    return false;
                }
            }

            if (numericRule && identifier.Length > 1 && identifier[0] == '0' && IsDigits(identifier))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryNumber(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        return !text.IsEmpty
            && IsDigits(text)
            && (text.Length == 1 || text[0] != '0')
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsDigits(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return !text.IsEmpty;
    }
}
