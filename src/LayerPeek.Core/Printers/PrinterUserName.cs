namespace LayerPeek.Core.Printers;

/// <summary>
/// Rules for the user name used to log in to PrusaLink (<see cref="PrinterDefinition.UserName"/>). Shared by the domain
/// model, settings loading and the settings window so that the rules exist only once.
/// </summary>
public static class PrinterUserName
{
    /// <summary>User name of the printer firmware's default account; used when none is configured.</summary>
    public const string Default = "maker";

    /// <summary>Maximum length in characters, after trimming.</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// Checks <paramref name="value"/> as it would be stored, i.e. after trimming surrounding white space.
    /// Allowed are printable ASCII characters except <c>:</c> (separates user and password in Digest and URLs),
    /// <c>"</c> and <c>\</c> (would break the quoted Digest header). Non-ASCII characters cannot be sent in an HTTP header.
    /// </summary>
    /// <returns>The first rule the value breaks, or <see cref="PrinterUserNameError.None"/> when it is valid.</returns>
    public static PrinterUserNameError Validate(string? value)
    {
        ReadOnlySpan<char> name = value.AsSpan().Trim();
        if (name.IsEmpty)
        {
            return PrinterUserNameError.Empty;
        }

        if (name.Length > MaxLength)
        {
            return PrinterUserNameError.TooLong;
        }

        PrinterUserNameError error = PrinterUserNameError.None;
        foreach (char c in name)
        {
            if (char.IsControl(c))
            {
                // Reported with priority: control characters are invisible in a text box.
                return PrinterUserNameError.ContainsControlCharacter;
            }

            if (error != PrinterUserNameError.None)
            {
                continue;
            }

            if (c == ':')
            {
                error = PrinterUserNameError.ContainsColon;
            }
            else if (c is '"' or '\\' || !char.IsAscii(c))
            {
                error = PrinterUserNameError.ContainsUnsupportedCharacter;
            }
        }

        return error;
    }

    /// <summary>True when <see cref="Validate"/> reports no error.</summary>
    public static bool IsValid(string? value) => Validate(value) == PrinterUserNameError.None;
}

/// <summary>Why a PrusaLink user name is rejected, see <see cref="PrinterUserName.Validate"/>.</summary>
public enum PrinterUserNameError
{
    None,

    /// <summary>Empty or white space only.</summary>
    Empty,

    /// <summary>Longer than <see cref="PrinterUserName.MaxLength"/> characters after trimming.</summary>
    TooLong,

    /// <summary>Contains a control character (e.g. tab, line break).</summary>
    ContainsControlCharacter,

    /// <summary>Contains <c>:</c>.</summary>
    ContainsColon,

    /// <summary>Contains <c>"</c>, <c>\</c> or a non-ASCII character.</summary>
    ContainsUnsupportedCharacter,
}
