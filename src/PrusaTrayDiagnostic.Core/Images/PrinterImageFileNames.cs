using System.Security.Cryptography;
using System.Text;

namespace PrusaTrayDiagnostic.Core.Images;

/// <summary>
/// File name of the custom image of a printer, derived from its id so that no id can leave the image folder or clash
/// with another printer's file.
/// </summary>
/// <remarks>
/// Ids are unique case-insensitively, and Windows and macOS file systems ignore case, so names are lowercase. An id of
/// 1–64 ASCII letters, digits and hyphens (e.g. the 32 hex digits the app generates) is used as is: <c>&lt;id&gt;.png</c>.
/// Any other id (path characters, dots, spaces, non-ASCII, too long, Windows device names like <c>con</c>) becomes
/// <c>h_&lt;32 hex digits of SHA-256 of the upper-case id&gt;.png</c>; the underscore never occurs in the first form.
/// </remarks>
public static class PrinterImageFileNames
{
    public const string Extension = ".png";

    private const int MaxPlainLength = 64;
    private const string HashedPrefix = "h_";

    private static readonly string[] ReservedNames =
    [
        "con", "prn", "aux", "nul",
        "com0", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt0", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    ];

    /// <exception cref="ArgumentException"><paramref name="printerId"/> is empty or white space.</exception>
    public static string For(string printerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        return (IsPlain(printerId) ? printerId.ToLowerInvariant() : Hashed(printerId)) + Extension;
    }

    private static bool IsPlain(string id)
    {
        if (id.Length > MaxPlainLength)
        {
            return false;
        }

        foreach (char c in id)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }

        return Array.IndexOf(ReservedNames, id.ToLowerInvariant()) < 0;
    }

    private static string Hashed(string id)
    {
        // Upper case: the same for every spelling of an id that compares equal case-insensitively (ordinal).
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(id.ToUpperInvariant()));
        return HashedPrefix + Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}
