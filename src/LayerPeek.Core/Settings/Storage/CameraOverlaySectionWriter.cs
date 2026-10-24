using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.Core.Settings.Storage;

/// <summary>
/// Sets <c>printers[*].camera.overlay</c> of one printer in a raw settings file without touching anything else (T-077: the
/// layout edited in the camera window is saved outside the settings window, byte for byte like
/// <see cref="CameraMonitorPathWriter"/>): the bytes of each existing <c>overlay</c> value of the camera object are
/// replaced, or the property is added after the last value of the camera object, indented like its first property. The
/// value is written like a full save writes it (format 2; <c>{}</c> for the default layout), indented relative to its line
/// when the camera object spans several lines, otherwise on one line; the line breaks follow the file. Comments,
/// formatting, unknown fields and the <c>version</c> stay (format 1 files are read with the widget shape too). A file of a
/// newer format (root <c>version</c> above <see cref="SettingsDocument.CurrentVersion"/>) or with an unreadable version is
/// left alone: this write must not slip a format-2 section into it unseen. Property names are matched case-insensitively
/// and escapes are decoded first, like <see cref="SettingsJsonContext"/> reads them.
/// </summary>
/// <remarks>
/// Every printer entry whose (last) <c>id</c> equals the printer id (trimmed, case-insensitive) is updated, in every
/// <c>camera</c> object it has and every <c>overlay</c> property of it (duplicates included). An entry without a
/// <c>camera</c> object is not changed.
/// </remarks>
internal static class CameraOverlaySectionWriter
{
    private const string PropertyName = "overlay";

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The content with <paramref name="layout"/> as the overlay of <paramref name="printerId"/>, or <see langword="null"/>
    /// when no camera object of that printer exists or nothing would change (every overlay value already has exactly these
    /// bytes, or the default layout and no overlay property).
    /// </summary>
    /// <exception cref="JsonException">The content is not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">Invalid UTF-8 in a property name or printer id.</exception>
    public static byte[]? Write(byte[] content, string printerId, CameraOverlayLayout layout)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(printerId);
        ArgumentNullException.ThrowIfNull(layout);
        string id = printerId.Trim();
        int offset = content.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        ReadOnlySpan<byte> json = content.AsSpan(offset);
        Value value = new(layout, json.IndexOf("\r\n"u8) >= 0 ? "\r\n"u8.ToArray() : "\n"u8.ToArray());
        List<Replacement> replacements = [];
        Utf8JsonReader reader = new(json, ReaderOptions);
        Stack<Frame> frames = new();
        string? property = null;

        // The root "version" as the loader reads it: the last of duplicates, quoted numbers accepted.
        int? version = null;
        bool unreadableVersion = false;

        while (reader.Read())
        {
            Frame? parent = frames.Count > 0 ? frames.Peek() : null;
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    property = reader.GetString();
                    if (parent is { Kind: FrameKind.Camera, FirstPropertyStart: < 0 })
                    {
                        parent.FirstPropertyStart = checked((int)reader.TokenStartIndex);
                    }

                    continue;

                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    unreadableVersion |= IsRootVersion(parent, property);
                    FrameKind kind = KindOf(parent, property, reader.TokenType);
                    frames.Push(new Frame(kind, checked((int)reader.TokenStartIndex), property, kind == FrameKind.Printer ? null : parent?.Printer));
                    break;

                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    Frame done = frames.Pop();
                    int end = checked((int)reader.BytesConsumed);
                    if (frames.Count > 0 && frames.Peek() is { Kind: FrameKind.Camera } camera)
                    {
                        // A nested object or array as a value of the camera.
                        camera.OnValue(done.Property, done.Start, end);
                    }

                    if (done.Kind == FrameKind.Camera)
                    {
                        done.Printer?.Cameras.Add(done);
                    }
                    else if (done.Kind == FrameKind.Printer
                        && done.Id is { } entryId && string.Equals(entryId.Trim(), id, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (Frame printerCamera in done.Cameras)
                        {
                            printerCamera.Update(json, value, offset, replacements);
                        }
                    }

                    break;

                default:
                    if (IsRootVersion(parent, property))
                    {
                        // An unreadable value fails the whole load, even when a later duplicate is readable.
                        if (TryReadVersion(ref reader, out int? read))
                        {
                            version = read;
                        }
                        else
                        {
                            unreadableVersion = true;
                        }
                    }
                    else if (parent?.Kind == FrameKind.Printer && reader.TokenType == JsonTokenType.String && Is(property, "id"))
                    {
                        parent.Id = reader.GetString();
                    }
                    else if (parent?.Kind == FrameKind.Camera)
                    {
                        parent.OnValue(property, checked((int)reader.TokenStartIndex), checked((int)reader.BytesConsumed));
                    }

                    break;
            }

            property = null;
        }

        // A file of a newer format, or one whose format cannot be read, is left alone: a full save of a newer file loses its
        // unknown values only after the loader warned about it, and this write would slip a format-2 section in unseen.
        if (unreadableVersion || version > SettingsDocument.CurrentVersion)
        {
            return null;
        }

        return replacements.Count == 0 ? null : Apply(content, replacements);
    }

    private static bool IsRootVersion(Frame? parent, string? property) => parent?.Kind == FrameKind.Root && Is(property, "version");

    /// <summary>A version value read like <see cref="SettingsDocument.Version"/>: a whole number (also quoted) or null.</summary>
    private static bool TryReadVersion(ref Utf8JsonReader reader, out int? version)
    {
        version = null;
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return true;
            case JsonTokenType.Number when reader.TryGetInt32(out int number):
                version = number;
                return true;
            case JsonTokenType.String when int.TryParse(reader.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int quoted):
                version = quoted;
                return true;
            default:
                return false;
        }
    }

    private static FrameKind KindOf(Frame? parent, string? property, JsonTokenType token)
    {
        bool isObject = token == JsonTokenType.StartObject;
        return parent?.Kind switch
        {
            null => isObject ? FrameKind.Root : FrameKind.Other,
            FrameKind.Root when !isObject && Is(property, "printers") => FrameKind.Printers,
            FrameKind.Printers when isObject => FrameKind.Printer,
            FrameKind.Printer when isObject && Is(property, "camera") => FrameKind.Camera,
            _ => FrameKind.Other,
        };
    }

    private static bool Is(string? property, string name) => string.Equals(property, name, StringComparison.OrdinalIgnoreCase);

    private static byte[] Apply(byte[] content, List<Replacement> replacements)
    {
        replacements.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        ArrayBufferWriter<byte> output = new(content.Length + 1024);
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

    /// <summary>The white space at the start of the line that contains <paramref name="position"/>.</summary>
    private static ReadOnlySpan<byte> IndentationOfLine(ReadOnlySpan<byte> json, int position)
    {
        int lineStart = json[..position].LastIndexOf((byte)'\n') + 1;
        ReadOnlySpan<byte> line = json[lineStart..position];
        int length = line.IndexOfAnyExcept(" \t"u8);
        return length < 0 ? line : line[..length];
    }

    private enum FrameKind
    {
        Root,
        Printers,
        Printer,
        Camera,
        Other,
    }

    /// <param name="Start">Position in the whole content (after a byte order mark).</param>
    /// <param name="Length">Bytes replaced; 0 inserts.</param>
    private readonly record struct Replacement(int Start, int Length, byte[] Bytes);

    /// <summary>The overlay value to write, formatted on demand for each indentation.</summary>
    private sealed class Value(CameraOverlayLayout layout, byte[] newLine)
    {
        private readonly CameraOverlayDocument? _document = SettingsMapper.ToDocument(layout);

        public bool IsDefault => _document is null;

        /// <summary>The value on one line, or indented below a line indented with <paramref name="indentation"/>.</summary>
        public byte[] Format(bool indented, ReadOnlySpan<byte> indentation)
        {
            if (_document is null)
            {
                return "{}"u8.ToArray();
            }

            ArrayBufferWriter<byte> buffer = new(1024);
            using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = indented, NewLine = "\n" }))
            {
                JsonSerializer.Serialize(writer, _document, SettingsJsonContext.Default.CameraOverlayDocument);
            }

            if (!indented)
            {
                return buffer.WrittenSpan.ToArray();
            }

            ArrayBufferWriter<byte> output = new(buffer.WrittenCount * 2);
            foreach (byte b in buffer.WrittenSpan)
            {
                if (b == (byte)'\n')
                {
                    output.Write(newLine);
                    output.Write(indentation);
                }
                else
                {
                    output.GetSpan(1)[0] = b;
                    output.Advance(1);
                }
            }

            return output.WrittenSpan.ToArray();
        }
    }

    /// <param name="start">Position of the opening bracket in the JSON (without a byte order mark).</param>
    /// <param name="property">Name of the property whose value this object or array is.</param>
    /// <param name="printer">The printer entry it is in; <see langword="null"/> for a printer entry itself (it is its own).</param>
    private sealed class Frame(FrameKind kind, int start, string? property, Frame? printer)
    {
        public FrameKind Kind { get; } = kind;

        public int Start { get; } = start;

        public string? Property { get; } = property;

        /// <summary>The printer entry this frame belongs to (itself for a printer entry).</summary>
        public Frame? Printer => Kind == FrameKind.Printer ? this : printer;

        /// <summary>The last <c>id</c> string of a printer entry (the loader uses the last of duplicates too).</summary>
        public string? Id { get; set; }

        /// <summary>The camera objects of a printer entry (its id may come after them).</summary>
        public List<Frame> Cameras { get; } = [];

        public int FirstPropertyStart { get; set; } = -1;

        /// <summary>End of the last value of a camera object; -1 while it has none.</summary>
        public int LastValueEnd { get; private set; } = -1;

        /// <summary>The <c>overlay</c> values of a camera object.</summary>
        public List<(int Start, int Length)> Values { get; } = [];

        public void OnValue(string? name, int start, int end)
        {
            LastValueEnd = end;
            if (Is(name, PropertyName))
            {
                Values.Add((start, end - start));
            }
        }

        public void Update(ReadOnlySpan<byte> json, Value value, int offset, List<Replacement> replacements)
        {
            // Indented when the camera object puts its first property on a new line (as a full save does).
            ReadOnlySpan<byte> lead = FirstPropertyStart < 0 ? [] : json[(Start + 1)..FirstPropertyStart];
            bool indented = lead.IndexOf((byte)'\n') >= 0;
            if (Values.Count > 0)
            {
                foreach ((int start, int length) in Values)
                {
                    byte[] bytes = value.Format(indented, IndentationOfLine(json, start));
                    if (!json.Slice(start, length).SequenceEqual(bytes))
                    {
                        replacements.Add(new Replacement(offset + start, length, bytes));
                    }
                }

                return;
            }

            if (value.IsDefault)
            {
                // No section means the default layout already.
                return;
            }

            byte[] name = [.. "\""u8, .. Encoding.UTF8.GetBytes(PropertyName), .. "\": "u8];
            if (LastValueEnd < 0)
            {
                // An empty camera object: the section becomes its only property, on the same line.
                replacements.Add(new Replacement(offset + Start + 1, 0, [.. " "u8, .. name, .. value.Format(indented: false, [])]));
                return;
            }

            // Indented like the first property when only white space precedes it, otherwise on the same line.
            ReadOnlySpan<byte> separator = lead.Length > 0 && lead.IndexOfAnyExcept(" \t\r\n"u8) < 0 ? lead : " "u8;
            ReadOnlySpan<byte> indentation = separator[(separator.LastIndexOf((byte)'\n') + 1)..];
            byte[] inserted = [.. ","u8, .. separator, .. name, .. value.Format(indented, indentation)];
            replacements.Add(new Replacement(offset + LastValueEnd, 0, inserted));
        }
    }
}
