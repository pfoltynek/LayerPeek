using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace LayerPeek.Core.Printers;

/// <summary>
/// Printer model code of Buddy firmware, "type.version.subversion" (e.g. <c>1.4.1</c> = MK4S), as PrusaLink reports it
/// in the <c>printer</c> field of <c>GET /api/version</c> since firmware 6.5. Each part is 0–255 (a byte in the firmware).
/// </summary>
public readonly record struct PrinterModelCode
{
    private const int MaxPartDigits = 3;

    /// <exception cref="ArgumentOutOfRangeException">A part is outside 0–255.</exception>
    public PrinterModelCode(int type, int version, int subversion)
    {
        CheckPart(type, nameof(type));
        CheckPart(version, nameof(version));
        CheckPart(subversion, nameof(subversion));
        Type = type;
        Version = version;
        Subversion = subversion;
    }

    public int Type { get; }

    public int Version { get; }

    public int Subversion { get; }

    /// <summary>
    /// Parses exactly three dot-separated decimal parts of 1–3 ASCII digits, each at most 255. Signs, spaces and any other
    /// characters are rejected (the firmware formats the code with <c>%i.%i.%i</c>).
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out PrinterModelCode code)
    {
        code = default;
        if (text is null)
        {
            return false;
        }

        ReadOnlySpan<char> rest = text;
        Span<int> parts = stackalloc int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            bool last = i == parts.Length - 1;
            int end = last ? rest.Length : rest.IndexOf('.');
            if (end < 0 || !TryParsePart(rest[..end], out parts[i]))
            {
                return false;
            }

            rest = last ? [] : rest[(end + 1)..];
        }

        code = new PrinterModelCode(parts[0], parts[1], parts[2]);
        return true;
    }

    /// <summary>"type.version.subversion", e.g. <c>7.1.0</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Type}.{Version}.{Subversion}");

    private static void CheckPart(int value, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, byte.MaxValue, name);
    }

    private static bool TryParsePart(ReadOnlySpan<char> part, out int value)
    {
        value = 0;
        if (part.IsEmpty || part.Length > MaxPartDigits)
        {
            return false;
        }

        foreach (char c in part)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            value = (value * 10) + (c - '0');
        }

        return value <= byte.MaxValue;
    }
}
