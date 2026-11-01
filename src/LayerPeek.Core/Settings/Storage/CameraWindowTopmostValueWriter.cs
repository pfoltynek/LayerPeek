using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Settings.Storage;

/// <summary>
/// Sets <c>printers[*].camera.window.topmostNormal</c> or <c>topmostMini</c> of one printer in a raw settings file without
/// touching anything else (T-117: the option switched in the camera window is saved outside the settings window, byte for
/// byte like <see cref="CameraOverlaySectionWriter"/>): the bytes of each existing value are replaced, or the property is
/// added after the last value of the window object, indented like its first property. A camera object without a window
/// section (a full save leaves it out while every window value is the default) gets one with only this value; so does a
/// <c>"window": null</c>. A missing value or section already means the default and stays missing for it. Comments,
/// formatting, unknown fields and the <c>version</c> stay; the line breaks follow the file. A file of a newer format (root
/// <c>version</c> above <see cref="SettingsDocument.CurrentVersion"/>) or with an unreadable version is left alone, like
/// <see cref="CameraOverlaySectionWriter"/> does. Property names are matched case-insensitively and escapes are decoded
/// first, like <see cref="SettingsJsonContext"/> reads them.
/// </summary>
/// <remarks>
/// Every printer entry whose (last) <c>id</c> equals the printer id (trimmed, case-insensitive) is updated, in every
/// <c>camera</c> object it has and every <c>window</c> object and value of it (duplicates included). An entry without a
/// <c>camera</c> object is not changed.
/// </remarks>
internal static class CameraWindowTopmostValueWriter
{
    private const string WindowName = "window";

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// <see cref="CameraWindowTopmostStoreResult.Written"/> with the content that has <paramref name="topmost"/> as the option
    /// of <paramref name="mode"/> of <paramref name="printerId"/> in <paramref name="written"/>;
    /// <see cref="CameraWindowTopmostStoreResult.AlreadyStored"/> when every camera of that printer holds it already; or
    /// <see cref="CameraWindowTopmostStoreResult.LeftAlone"/> when no camera object of that printer exists, the format is
    /// newer or unreadable, or a <c>window</c> of such a camera is neither an object nor <c>null</c> (an array or a scalar:
    /// the loader rejects it, and adding a section would only duplicate the key).
    /// </summary>
    /// <exception cref="JsonException">The content is not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">Invalid UTF-8 in a property name or printer id.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is full screen.</exception>
    public static CameraWindowTopmostStoreResult Write(byte[] content, string printerId, CameraWindowMode mode, bool topmost, out byte[]? written)
    {
        written = null;
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(printerId);
        (string name, bool defaultValue) = mode switch
        {
            CameraWindowMode.Normal => ("topmostNormal", CameraWindowDefaults.Default.TopmostNormal),
            CameraWindowMode.Mini => ("topmostMini", CameraWindowDefaults.Default.TopmostMini),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Only the normal window and the mini preview have an always-on-top option."),
        };

        string id = printerId.Trim();
        int offset = content.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        ReadOnlySpan<byte> json = content.AsSpan(offset);
        Value value = new(name, topmost, topmost == defaultValue, json.IndexOf("\r\n"u8) >= 0 ? "\r\n"u8.ToArray() : "\n"u8.ToArray());
        List<Replacement> replacements = [];
        Utf8JsonReader reader = new(json, ReaderOptions);
        Stack<Frame> frames = new();
        string? property = null;

        // The root "version" as the loader reads it: the last of duplicates, quoted numbers accepted.
        int? version = null;
        bool unreadableVersion = false;

        // Camera objects of the printer, and whether one of them has a window value of another type.
        int cameras = 0;
        bool otherWindow = false;

        while (reader.Read())
        {
            Frame? parent = frames.Count > 0 ? frames.Peek() : null;
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    property = reader.GetString();
                    if (parent is { Kind: FrameKind.Camera or FrameKind.Window, FirstPropertyStart: < 0 })
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
                    if (frames.Count > 0 && frames.Peek() is { Kind: FrameKind.Camera or FrameKind.Window } container)
                    {
                        // A nested object or array as a value of the camera or of its window.
                        container.OnValue(done.Property, done.Start, end, name);
                        if (done.Kind == FrameKind.Window)
                        {
                            container.Windows.Add(done);
                        }
                        else if (container.Kind == FrameKind.Camera && Is(done.Property, WindowName))
                        {
                            // "window": [ … ]
                            container.HasOtherWindow = true;
                        }
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
                            cameras++;
                            otherWindow |= printerCamera.HasOtherWindow;
                            printerCamera.UpdateCamera(json, value, offset, replacements);
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
                    else if (parent?.Kind is FrameKind.Camera or FrameKind.Window)
                    {
                        int start = checked((int)reader.TokenStartIndex);
                        int valueEnd = checked((int)reader.BytesConsumed);
                        parent.OnValue(property, start, valueEnd, name);
                        if (parent.Kind == FrameKind.Camera && Is(property, WindowName))
                        {
                            if (reader.TokenType == JsonTokenType.Null)
                            {
                                // "window": null reads as the defaults: replaced by a section with the value.
                                parent.NullWindows.Add((start, valueEnd - start));
                            }
                            else
                            {
                                // A string, number or boolean.
                                parent.HasOtherWindow = true;
                            }
                        }
                    }

                    break;
            }

            property = null;
        }

        // A file of a newer format, or one whose format cannot be read, is left alone (see CameraOverlaySectionWriter).
        if (unreadableVersion || version > SettingsDocument.CurrentVersion || cameras == 0 || otherWindow)
        {
            return CameraWindowTopmostStoreResult.LeftAlone;
        }

        if (replacements.Count == 0)
        {
            return CameraWindowTopmostStoreResult.AlreadyStored;
        }

        written = Apply(content, replacements);
        return CameraWindowTopmostStoreResult.Written;
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
            FrameKind.Camera when isObject && Is(property, WindowName) => FrameKind.Window,
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
        Window,
        Other,
    }

    /// <param name="Start">Position in the whole content (after a byte order mark).</param>
    /// <param name="Length">Bytes replaced; 0 inserts.</param>
    private readonly record struct Replacement(int Start, int Length, byte[] Bytes);

    /// <summary>The value to write, and a window section with only it.</summary>
    private sealed class Value(string name, bool topmost, bool isDefault, byte[] newLine)
    {
        public string Name { get; } = name;

        /// <summary>A missing value (or window section) means this value already.</summary>
        public bool IsDefault { get; } = isDefault;

        public byte[] Literal { get; } = topmost ? "true"u8.ToArray() : "false"u8.ToArray();

        /// <summary><c>"name": value</c>.</summary>
        public byte[] Property => [.. "\""u8, .. Encoding.UTF8.GetBytes(Name), .. "\": "u8, .. Literal];

        /// <summary>
        /// A window section with only the value: on one line, or (as a full save writes it) below a line indented with
        /// <paramref name="indentation"/>, the value two spaces further in.
        /// </summary>
        public byte[] Section(bool indented, ReadOnlySpan<byte> indentation) => indented
            ? [.. "{"u8, .. newLine, .. indentation, .. "  "u8, .. Property, .. newLine, .. indentation, .. "}"u8]
            : [.. "{ "u8, .. Property, .. " }"u8];
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

        /// <summary>The window objects of a camera object.</summary>
        public List<Frame> Windows { get; } = [];

        /// <summary>The <c>window</c> values of a camera object that are <c>null</c>.</summary>
        public List<(int Start, int Length)> NullWindows { get; } = [];

        /// <summary>A <c>window</c> value of a camera object that is neither an object nor <c>null</c>.</summary>
        public bool HasOtherWindow { get; set; }

        public int FirstPropertyStart { get; set; } = -1;

        /// <summary>End of the last value of a camera or window object; -1 while it has none.</summary>
        public int LastValueEnd { get; private set; } = -1;

        /// <summary>The values of the option in a window object.</summary>
        public List<(int Start, int Length)> Values { get; } = [];

        public void OnValue(string? name, int start, int end, string optionName)
        {
            LastValueEnd = end;
            if (Kind == FrameKind.Window && Is(name, optionName))
            {
                Values.Add((start, end - start));
            }
        }

        public void UpdateCamera(ReadOnlySpan<byte> json, Value value, int offset, List<Replacement> replacements)
        {
            foreach (Frame window in Windows)
            {
                window.UpdateWindow(json, value, offset, replacements);
            }

            if (value.IsDefault)
            {
                // No section (or a null one) means the default already.
                return;
            }

            bool indented = IsIndented(json);
            foreach ((int start, int length) in NullWindows)
            {
                replacements.Add(new Replacement(offset + start, length, value.Section(indented, IndentationOfLine(json, start))));
            }

            if (Windows.Count > 0 || NullWindows.Count > 0)
            {
                return;
            }

            byte[] name = [.. "\""u8, .. Encoding.UTF8.GetBytes(WindowName), .. "\": "u8];
            if (LastValueEnd < 0)
            {
                // An empty camera object: the section becomes its only property, on the same line.
                replacements.Add(new Replacement(offset + Start + 1, 0, [.. " "u8, .. name, .. value.Section(indented: false, [])]));
                return;
            }

            (byte[] separator, byte[] indentation) = Separator(json);
            replacements.Add(new Replacement(offset + LastValueEnd, 0, [.. ","u8, .. separator, .. name, .. value.Section(indented, indentation)]));
        }

        private void UpdateWindow(ReadOnlySpan<byte> json, Value value, int offset, List<Replacement> replacements)
        {
            if (Values.Count > 0)
            {
                foreach ((int start, int length) in Values)
                {
                    if (!json.Slice(start, length).SequenceEqual(value.Literal))
                    {
                        replacements.Add(new Replacement(offset + start, length, value.Literal));
                    }
                }

                return;
            }

            if (value.IsDefault)
            {
                // No value means the default already.
                return;
            }

            if (LastValueEnd < 0)
            {
                // An empty window object: the value becomes its only property, on the same line.
                replacements.Add(new Replacement(offset + Start + 1, 0, [.. " "u8, .. value.Property, .. " "u8]));
                return;
            }

            (byte[] separator, _) = Separator(json);
            replacements.Add(new Replacement(offset + LastValueEnd, 0, [.. ","u8, .. separator, .. value.Property]));
        }

        /// <summary>Indented when the object puts its first property on a new line (as a full save does).</summary>
        private bool IsIndented(ReadOnlySpan<byte> json) => FirstPropertyStart >= 0 && json[(Start + 1)..FirstPropertyStart].IndexOf((byte)'\n') >= 0;

        /// <summary>
        /// What goes between a new property and the value before it: the white space before the first property when there is
        /// only white space, otherwise one space; and the indentation of the new property.
        /// </summary>
        private (byte[] Separator, byte[] Indentation) Separator(ReadOnlySpan<byte> json)
        {
            ReadOnlySpan<byte> lead = FirstPropertyStart < 0 ? [] : json[(Start + 1)..FirstPropertyStart];
            ReadOnlySpan<byte> separator = lead.Length > 0 && lead.IndexOfAnyExcept(" \t\r\n"u8) < 0 ? lead : " "u8;
            return (separator.ToArray(), separator[(separator.LastIndexOf((byte)'\n') + 1)..].ToArray());
        }
    }
}
