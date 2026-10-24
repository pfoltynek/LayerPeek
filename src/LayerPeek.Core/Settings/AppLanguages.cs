namespace LayerPeek.Core.Settings;

/// <summary>
/// Languages the application is translated into, as neutral culture codes (<c>en</c> is the neutral resources).
/// <see cref="AppSettings.Language"/> is one of them or <see langword="null"/> (follow the system).
/// </summary>
public static class AppLanguages
{
    /// <summary>Supported language codes, lowercase.</summary>
    public static IReadOnlyList<string> Supported { get; } = Array.AsReadOnly(["en", "cs", "sk", "de", "es", "fr", "it", "pl"]);

    /// <summary>
    /// Finds the supported code for <paramref name="value"/> (surrounding white space and case are ignored).
    /// Regional variants (<c>de-AT</c>) are not language settings and are not accepted.
    /// </summary>
    public static bool TryNormalize(string? value, out string? code)
    {
        ReadOnlySpan<char> trimmed = value.AsSpan().Trim();
        foreach (string supported in Supported)
        {
            if (trimmed.Equals(supported, StringComparison.OrdinalIgnoreCase))
            {
                code = supported;
                return true;
            }
        }

        code = null;
        return false;
    }

    /// <summary>Whether <paramref name="code"/> is exactly one of <see cref="Supported"/>.</summary>
    public static bool IsSupported(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        foreach (string supported in Supported)
        {
            if (string.Equals(code, supported, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
