using System.Buffers;
using System.Text;
using System.Text.Json;

using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Settings.Storage;

/// <summary>
/// Sets <c>printers[*].detectedModel</c> of one printer in a raw settings file without touching anything else (T-039:
/// the model detected by monitoring is saved in the background): the bytes of each existing <c>detectedModel</c> value of
/// the printer are replaced, or the property is added after the last value of the printer object, indented like its first
/// property. Comments, formatting, unknown fields and entries the mapper would skip stay byte for byte. Property names are
/// matched case-insensitively and escapes are decoded first, like <see cref="SettingsJsonContext"/> reads them.
/// </summary>
/// <remarks>
/// Every entry whose (last) <c>id</c> equals the printer id (trimmed, case-insensitive, as the mapper compares ids) is
/// updated, including duplicates the mapper skips; every <c>detectedModel</c> of such an entry is replaced, whatever its
/// JSON type.
/// </remarks>
internal static class DetectedModelWriter
{
    private const string PropertyName = "detectedModel";

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The content with <paramref name="code"/> as the detected model of <paramref name="printerId"/>, or
    /// <see langword="null"/> when no entry has that id or every one already has exactly this value. Only an entry that still
    /// describes the machine the code was detected on is changed: its (last) <c>address</c> equals
    /// <paramref name="expectedAddress"/> (trimmed, case-insensitive) and its <c>connectionType</c> names
    /// <paramref name="expectedConnectionType"/> (case-insensitive), so that a file changed meanwhile (another address)
    /// never gets the model of the previous machine.
    /// </summary>
    /// <exception cref="JsonException">The content is not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">Invalid UTF-8 in a property name or printer id.</exception>
    public static byte[]? Write(byte[] content, string printerId, PrinterModelCode code, string expectedAddress, ConnectionType expectedConnectionType)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(printerId);
        string id = printerId.Trim();
        ArgumentNullException.ThrowIfNull(expectedAddress);
        string address = expectedAddress.Trim();
        string connectionType = expectedConnectionType.ToString();
        byte[] value = Encoding.UTF8.GetBytes($"\"{code}\"");
        int offset = content.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        ReadOnlySpan<byte> json = content.AsSpan(offset);
        List<Replacement> replacements = [];
        Utf8JsonReader reader = new(json, ReaderOptions);
        Stack<Frame> frames = new();
        string? property = null;

        while (reader.Read())
        {
            Frame? parent = frames.Count > 0 ? frames.Peek() : null;
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    property = reader.GetString();
                    if (parent is { Kind: FrameKind.Printer, FirstPropertyStart: < 0 })
                    {
                        parent.FirstPropertyStart = checked((int)reader.TokenStartIndex);
                    }

                    continue;

                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    frames.Push(new Frame(KindOf(parent, property, reader.TokenType), checked((int)reader.TokenStartIndex), property));
                    break;

                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    Frame done = frames.Pop();
                    int end = checked((int)reader.BytesConsumed);
                    if (frames.Count > 0 && frames.Peek() is { Kind: FrameKind.Printer } printer)
                    {
                        // A nested object or array as a value of the printer.
                        printer.OnValue(done.Property, done.Start, end);
                    }

                    if (done.Kind == FrameKind.Printer
                        && done.Id is { } entryId && string.Equals(entryId.Trim(), id, StringComparison.OrdinalIgnoreCase)
                        && done.Address is { } entryAddress && string.Equals(entryAddress.Trim(), address, StringComparison.OrdinalIgnoreCase)
                        && done.ConnectionType is { } entryType && string.Equals(entryType.Trim(), connectionType, StringComparison.OrdinalIgnoreCase))
                    {
                        done.Update(json, value, offset, replacements);
                    }

                    break;

                default:
                    if (parent?.Kind == FrameKind.Printer)
                    {
                        if (reader.TokenType == JsonTokenType.String && Is(property, "id"))
                        {
                            parent.Id = reader.GetString();
                        }
                        else if (reader.TokenType == JsonTokenType.String && Is(property, "address"))
                        {
                            parent.Address = reader.GetString();
                        }
                        else if (reader.TokenType == JsonTokenType.String && Is(property, "connectionType"))
                        {
                            parent.ConnectionType = reader.GetString();
                        }

                        parent.OnValue(property, checked((int)reader.TokenStartIndex), checked((int)reader.BytesConsumed));
                    }

                    break;
            }

            property = null;
        }

        return replacements.Count == 0 ? null : Apply(content, replacements);
    }

    private static FrameKind KindOf(Frame? parent, string? property, JsonTokenType token)
    {
        bool isObject = token == JsonTokenType.StartObject;
        return parent?.Kind switch
        {
            null => isObject ? FrameKind.Root : FrameKind.Other,
            FrameKind.Root when !isObject && Is(property, "printers") => FrameKind.Printers,
            FrameKind.Printers when isObject => FrameKind.Printer,
            _ => FrameKind.Other,
        };
    }

    private static bool Is(string? property, string name) => string.Equals(property, name, StringComparison.OrdinalIgnoreCase);

    private static byte[] Apply(byte[] content, List<Replacement> replacements)
    {
        replacements.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        ArrayBufferWriter<byte> output = new(content.Length + 64);
        int position = 0;
        foreach (Replacement replacement in replacements)
        {
            output.Write(content.AsSpan(position, replacement.Start - position));
            output.Write(replacement.Bytes);
            position = replacement.Start + replacement.Length;
        }

        output.Write(content.AsSpan(position));
        return output.WrittenSpan.ToArray();
    }

    private enum FrameKind
    {
        Root,
        Printers,
        Printer,
        Other,
    }

    /// <param name="Start">Position in the whole content (after a byte order mark).</param>
    /// <param name="Length">Bytes replaced; 0 inserts.</param>
    private readonly record struct Replacement(int Start, int Length, byte[] Bytes);

    /// <param name="Start">Position of the opening bracket in the JSON (without a byte order mark).</param>
    /// <param name="Property">Name of the property whose value this object or array is.</param>
    private sealed class Frame(FrameKind kind, int start, string? property)
    {
        public FrameKind Kind { get; } = kind;

        public int Start { get; } = start;

        public string? Property { get; } = property;

        /// <summary>The last <c>id</c> string of the printer entry (the loader uses the last of duplicates too).</summary>
        public string? Id { get; set; }

        /// <summary>The last <c>address</c> string of the printer entry.</summary>
        public string? Address { get; set; }

        /// <summary>The last <c>connectionType</c> string of the printer entry.</summary>
        public string? ConnectionType { get; set; }

        public int FirstPropertyStart { get; set; } = -1;

        /// <summary>End of the last value of the printer entry; -1 while it has none.</summary>
        public int LastValueEnd { get; private set; } = -1;

        public List<(int Start, int Length)> Values { get; } = [];

        public void OnValue(string? property, int start, int end)
        {
            LastValueEnd = end;
            if (Is(property, PropertyName))
            {
                Values.Add((start, end - start));
            }
        }

        public void Update(ReadOnlySpan<byte> json, byte[] value, int offset, List<Replacement> replacements)
        {
            if (Values.Count > 0)
            {
                foreach ((int start, int length) in Values)
                {
                    if (!json.Slice(start, length).SequenceEqual(value))
                    {
                        replacements.Add(new Replacement(offset + start, length, value));
                    }
                }

                return;
            }

            if (LastValueEnd < 0 || FirstPropertyStart < 0)
            {
                // An entry with an id always has a value; defensive only.
                return;
            }

            // Indented like the first property when only white space precedes it, otherwise on the same line.
            ReadOnlySpan<byte> lead = json[(Start + 1)..FirstPropertyStart];
            ReadOnlySpan<byte> separator = lead.Length > 0 && lead.IndexOfAnyExcept(" \t\r\n"u8) < 0 ? lead : " "u8;
            byte[] inserted = [.. ","u8, .. separator, .. "\""u8, .. Encoding.UTF8.GetBytes(PropertyName), .. "\": "u8, .. value];
            replacements.Add(new Replacement(offset + LastValueEnd, 0, inserted));
        }
    }
}
