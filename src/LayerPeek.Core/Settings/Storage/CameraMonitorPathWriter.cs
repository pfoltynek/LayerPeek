using System.Buffers;
using System.Text;
using System.Text.Json;

namespace LayerPeek.Core.Settings.Storage;

/// <summary>
/// Sets <c>printers[*].camera.window.monitor.devicePath</c> of one printer in a raw settings file without touching anything
/// else (user decision 2026-10-23: the device path of a monitor found on another connector is saved in the background, byte
/// for byte like <see cref="DetectedModelWriter"/>): the bytes of each existing <c>devicePath</c> value of the monitor are
/// replaced, or the property is added after the last value of the monitor object, indented like its first property.
/// Comments, formatting and unknown fields stay. Property names are matched case-insensitively and escapes are decoded
/// first, like <see cref="SettingsJsonContext"/> reads them; the path is escaped like the serializer writes it.
/// </summary>
/// <remarks>
/// Every printer entry whose (last) <c>id</c> equals the printer id (trimmed, case-insensitive) is updated, in every
/// <c>camera</c> / <c>window</c> / <c>monitor</c> object it has (duplicates included). The caller checks first that the
/// file loads without warnings and still has the expected monitor.
/// </remarks>
internal static class CameraMonitorPathWriter
{
    private const string PropertyName = "devicePath";

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The content with <paramref name="devicePath"/> as the camera monitor path of <paramref name="printerId"/>, or
    /// <see langword="null"/> when no monitor of that printer exists or every one already has exactly this value.
    /// </summary>
    /// <exception cref="JsonException">The content is not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">Invalid UTF-8 in a property name or printer id.</exception>
    public static byte[]? Write(byte[] content, string printerId, string devicePath)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(printerId);
        ArgumentNullException.ThrowIfNull(devicePath);
        string id = printerId.Trim();
        byte[] value = [.. "\""u8, .. JsonEncodedText.Encode(devicePath).EncodedUtf8Bytes, .. "\""u8];
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
                    if (parent is { Kind: FrameKind.Monitor, FirstPropertyStart: < 0 })
                    {
                        parent.FirstPropertyStart = checked((int)reader.TokenStartIndex);
                    }

                    continue;

                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    FrameKind kind = KindOf(parent, property, reader.TokenType);
                    frames.Push(new Frame(kind, checked((int)reader.TokenStartIndex), property, kind == FrameKind.Printer ? null : parent?.Printer));
                    break;

                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    Frame done = frames.Pop();
                    int end = checked((int)reader.BytesConsumed);
                    if (frames.Count > 0 && frames.Peek() is { Kind: FrameKind.Monitor } monitor)
                    {
                        // A nested object or array as a value of the monitor.
                        monitor.OnValue(done.Property, done.Start, end);
                    }

                    if (done.Kind == FrameKind.Monitor)
                    {
                        done.Printer?.Monitors.Add(done);
                    }
                    else if (done.Kind == FrameKind.Printer
                        && done.Id is { } entryId && string.Equals(entryId.Trim(), id, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (Frame printerMonitor in done.Monitors)
                        {
                            printerMonitor.Update(json, value, offset, replacements);
                        }
                    }

                    break;

                default:
                    if (parent?.Kind == FrameKind.Printer && reader.TokenType == JsonTokenType.String && Is(property, "id"))
                    {
                        parent.Id = reader.GetString();
                    }
                    else if (parent?.Kind == FrameKind.Monitor)
                    {
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
            FrameKind.Printer when isObject && Is(property, "camera") => FrameKind.Camera,
            FrameKind.Camera when isObject && Is(property, "window") => FrameKind.Window,
            FrameKind.Window when isObject && Is(property, "monitor") => FrameKind.Monitor,
            _ => FrameKind.Other,
        };
    }

    private static bool Is(string? property, string name) => string.Equals(property, name, StringComparison.OrdinalIgnoreCase);

    private static byte[] Apply(byte[] content, List<Replacement> replacements)
    {
        replacements.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        ArrayBufferWriter<byte> output = new(content.Length + 256);
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
        Camera,
        Window,
        Monitor,
        Other,
    }

    /// <param name="Start">Position in the whole content (after a byte order mark).</param>
    /// <param name="Length">Bytes replaced; 0 inserts.</param>
    private readonly record struct Replacement(int Start, int Length, byte[] Bytes);

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

        /// <summary>The monitor objects of a printer entry (its id may come after them).</summary>
        public List<Frame> Monitors { get; } = [];

        public int FirstPropertyStart { get; set; } = -1;

        /// <summary>End of the last value of a monitor object; -1 while it has none.</summary>
        public int LastValueEnd { get; private set; } = -1;

        public List<(int Start, int Length)> Values { get; } = [];

        public void OnValue(string? name, int start, int end)
        {
            LastValueEnd = end;
            if (Is(name, PropertyName))
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
                // An empty monitor object is not a monitor (the loader would have reported it); defensive only.
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
