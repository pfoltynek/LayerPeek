using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LayerPeek.Core.Settings.Storage;

/// <summary>
/// The root <c>startAtLogin</c> of a raw settings file (T-201) without the rest of the document: the install hook reads it
/// so that a type error elsewhere in the file (which makes the whole file unreadable for the loader) never loses a stored
/// "off", and the one-time migration of the app adds it without touching anything else. Property names are matched
/// case-insensitively and escapes are decoded first, like <see cref="SettingsJsonContext"/> reads them; of duplicates the
/// last one counts, as for the loader.
/// </summary>
internal static class StartAtLoginValue
{
    public const string PropertyName = "startAtLogin";

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The stored choice; <see langword="null"/> when the root is <c>null</c>, the value is missing or <c>null</c>.</summary>
    /// <exception cref="JsonException">
    /// The content is not valid JSON, its root is neither an object nor <c>null</c>, or <c>startAtLogin</c> is neither a
    /// boolean nor <c>null</c> (the loader would reject the whole file too).
    /// </exception>
    /// <exception cref="InvalidOperationException">Invalid UTF-8 in a property name.</exception>
    public static bool? Read(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> json = content.StartsWith(Utf8Bom) ? content[Utf8Bom.Length..] : content;
        Utf8JsonReader reader = new(json, ReaderOptions);
        if (!reader.Read())
        {
            throw new JsonException("The settings file is empty.");
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("The settings file holds no settings object.");
        }

        bool? choice = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool matches = Is(reader.GetString());
            reader.Read();
            if (matches)
            {
                choice = reader.TokenType switch
                {
                    JsonTokenType.True => true,
                    JsonTokenType.False => false,
                    JsonTokenType.Null => null,
                    _ => throw new JsonException($"'{PropertyName}' is not a boolean."),
                };
            }

            // Validates the rest of the value, so that a broken file is reported like the loader reports it.
            reader.Skip();
        }

        // The rest of the document must be valid JSON as well.
        while (reader.Read())
        {
        }

        return choice;
    }

    /// <summary>
    /// The content with <paramref name="choice"/> as <c>startAtLogin</c> when the file holds no choice yet (missing or
    /// <c>null</c>), or <see langword="null"/> to leave it alone: a value is stored already (whatever its type), the root is
    /// no object, or the file is of a newer or unreadable format (like the other writers of the settings file). Of duplicates
    /// the last one counts, as for <see cref="Read"/>: a last <c>null</c> is replaced. A missing value is added after the
    /// last property of the root object, indented like its first one, or into an empty root object right before its closing
    /// brace; comments, formatting and unknown fields stay byte for byte.
    /// </summary>
    /// <exception cref="JsonException">The content is not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">Invalid UTF-8 in a property name.</exception>
    public static byte[]? WriteIfUnset(byte[] content, bool choice)
    {
        ArgumentNullException.ThrowIfNull(content);
        int offset = content.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        ReadOnlySpan<byte> json = content.AsSpan(offset);
        Utf8JsonReader reader = new(json, ReaderOptions);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            // An empty file is invalid JSON for the loader; a root of another kind is not ours to change.
            Drain(ref reader);
            return null;
        }

        int rootStart = checked((int)reader.TokenStartIndex);
        int firstPropertyStart = -1;
        int lastValueEnd = -1;
        // The last startAtLogin counts (as for Read): a null to replace, or a stored value.
        (int Start, int Length)? nullValue = null;
        bool stored = false;
        bool leaveAlone = false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (firstPropertyStart < 0)
            {
                firstPropertyStart = checked((int)reader.TokenStartIndex);
            }

            string? name = reader.GetString();
            reader.Read();
            int valueStart = checked((int)reader.TokenStartIndex);
            if (Is(name))
            {
                stored = reader.TokenType != JsonTokenType.Null;
                nullValue = stored ? null : (valueStart, checked((int)reader.BytesConsumed) - valueStart);
            }
            else if (string.Equals(name, "version", StringComparison.OrdinalIgnoreCase))
            {
                leaveAlone |= !IsReadableVersion(ref reader);
            }

            reader.Skip();
            lastValueEnd = checked((int)reader.BytesConsumed);
        }

        int rootEnd = checked((int)reader.TokenStartIndex);
        Drain(ref reader);
        if (stored || leaveAlone)
        {
            return null;
        }

        byte[] value = Encoding.UTF8.GetBytes(choice ? "true" : "false");
        if (nullValue is { } existing)
        {
            return Splice(content, offset + existing.Start, existing.Length, value);
        }

        string property = $"\"{PropertyName}\": {(choice ? "true" : "false")}";
        if (lastValueEnd < 0)
        {
            ReadOnlySpan<byte> inside = json[(rootStart + 1)..rootEnd];
            if (inside.TrimStart(" \t\r\n"u8).IsEmpty)
            {
                // "{}" or "{ }": the only property.
                return Splice(content, offset + rootStart + 1, inside.Length, Encoding.UTF8.GetBytes($" {property} "));
            }

            // Only comments: they stay, the property goes right before the closing brace (never into a line comment, which
            // ends before the brace).
            string before = inside[^1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' ? string.Empty : " ";
            return Splice(content, offset + rootEnd, 0, Encoding.UTF8.GetBytes($"{before}{property} "));
        }

        string newline = json.IndexOf("\r\n"u8) >= 0 ? "\r\n" : "\n";
        int lineStart = json[..firstPropertyStart].LastIndexOf((byte)'\n') + 1;
        ReadOnlySpan<byte> indent = json[lineStart..firstPropertyStart];
        string separator = lineStart > rootStart && IsWhitespace(indent)
            ? newline + Encoding.UTF8.GetString(indent)
            : " ";
        return Splice(content, offset + lastValueEnd, 0, Encoding.UTF8.GetBytes($",{separator}{property}"));
    }

    private static bool Is(string? name) => string.Equals(name, PropertyName, StringComparison.OrdinalIgnoreCase);

    /// <summary>A <c>version</c> the loader reads (a whole number, also quoted, or null) of this or an older format.</summary>
    private static bool IsReadableVersion(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Null => true,
        JsonTokenType.Number => reader.TryGetInt32(out int number) && number <= SettingsDocument.CurrentVersion,
        JsonTokenType.String => int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int quoted) && quoted <= SettingsDocument.CurrentVersion,
        _ => false,
    };

    private static bool IsWhitespace(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            if (b is not ((byte)' ' or (byte)'\t'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads the rest, so that invalid JSON after the root object is reported.</summary>
    private static void Drain(ref Utf8JsonReader reader)
    {
        while (reader.Read())
        {
        }
    }

    private static byte[] Splice(byte[] content, int start, int length, byte[] replacement)
    {
        byte[] result = new byte[content.Length - length + replacement.Length];
        content.AsSpan(0, start).CopyTo(result);
        replacement.CopyTo(result.AsSpan(start));
        content.AsSpan(start + length).CopyTo(result.AsSpan(start + replacement.Length));
        return result;
    }
}
