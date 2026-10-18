using System.Buffers;
using System.Text.Json;

using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Core.Settings.Storage;

/// <summary>
/// Removes credentials from the camera addresses of a raw settings file without touching anything else: only the
/// bytes of each affected <c>printers[*].camera.streamUrl</c> value (and of <c>enabled</c> in the same camera object,
/// which becomes <c>false</c>) are replaced. Comments, formatting, unknown fields and entries the mapper would skip
/// stay byte for byte. Property names are matched case-insensitively, like <see cref="SettingsJsonContext"/> reads them.
/// </summary>
internal static class CameraCredentialScrubber
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] False = "false"u8.ToArray();

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Returns the cleaned file content, or <see langword="null"/> when no camera address contains credentials.
    /// <paramref name="changedPrinterIds"/> receives the ids of the affected printers (entries without an id are cleaned
    /// as well, but have no id to report).
    /// </summary>
    /// <exception cref="JsonException">The content is not valid JSON.</exception>
    public static byte[]? Scrub(byte[] content, List<string> changedPrinterIds)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(changedPrinterIds);
        int offset = content.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        List<Replacement> replacements = [];
        Utf8JsonReader reader = new(content.AsSpan(offset), ReaderOptions);
        Stack<Frame> frames = new();
        string? property = null;

        while (reader.Read())
        {
            Frame? parent = frames.Count > 0 ? frames.Peek() : null;
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    property = reader.GetString();
                    continue;

                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    frames.Push(new Frame(KindOf(parent, property, reader.TokenType)));
                    break;

                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    Frame done = frames.Pop();
                    if (done.Kind == FrameKind.Camera && done.Camera!.Scrub(offset, replacements) && frames.Peek().Kind == FrameKind.Printer)
                    {
                        frames.Peek().PrinterChanged = true;
                    }
                    else if (done.Kind == FrameKind.Printer && done.PrinterChanged && done.PrinterId is { } id)
                    {
                        changedPrinterIds.Add(id);
                    }

                    break;

                default:
                    OnValue(ref reader, parent, property);
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
            _ => FrameKind.Other,
        };
    }

    private static void OnValue(ref Utf8JsonReader reader, Frame? parent, string? property)
    {
        if (parent?.Kind == FrameKind.Printer && reader.TokenType == JsonTokenType.String && Is(property, "id"))
        {
            parent.PrinterId = reader.GetString();
        }
        else if (parent?.Kind == FrameKind.Camera)
        {
            // Strings: the token starts at the opening quote; ValueSpan is the raw (still escaped) text between the quotes.
            int start = checked((int)reader.TokenStartIndex);
            if (reader.TokenType == JsonTokenType.String && Is(property, "streamUrl"))
            {
                parent.Camera!.Urls.Add((start, reader.ValueSpan.Length + 2, reader.GetString()!));
            }
            else if (reader.TokenType is JsonTokenType.True && Is(property, "enabled"))
            {
                parent.Camera!.EnabledTrue.Add((start, reader.ValueSpan.Length));
            }
        }
    }

    private static bool Is(string? property, string name) => string.Equals(property, name, StringComparison.OrdinalIgnoreCase);

    private static byte[] Apply(byte[] content, List<Replacement> replacements)
    {
        replacements.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        ArrayBufferWriter<byte> output = new(content.Length);
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

    private static byte[] EncodeString(string value)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStringValue(value);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private enum FrameKind
    {
        Root,
        Printers,
        Printer,
        Camera,
        Other,
    }

    private readonly record struct Replacement(int Start, int Length, byte[] Bytes);

    private sealed class Frame(FrameKind kind)
    {
        public FrameKind Kind { get; } = kind;

        public string? PrinterId { get; set; }

        public bool PrinterChanged { get; set; }

        public CameraValues? Camera { get; } = kind == FrameKind.Camera ? new CameraValues() : null;
    }

    /// <summary>Positions inside one camera object; duplicate properties are all handled.</summary>
    private sealed class CameraValues
    {
        public List<(int Start, int Length, string Value)> Urls { get; } = [];

        public List<(int Start, int Length)> EnabledTrue { get; } = [];

        /// <returns>Whether an address had credentials (then the camera is also switched off).</returns>
        public bool Scrub(int offset, List<Replacement> replacements)
        {
            bool changed = false;
            foreach ((int start, int length, string value) in Urls)
            {
                if (CameraSettings.RemoveUserInfo(value, out string withoutCredentials))
                {
                    replacements.Add(new Replacement(offset + start, length, EncodeString(withoutCredentials)));
                    changed = true;
                }
            }

            if (changed)
            {
                // Same rule as when loading: the camera stays off until the user reviews the address.
                foreach ((int start, int length) in EnabledTrue)
                {
                    replacements.Add(new Replacement(offset + start, length, False));
                }
            }

            return changed;
        }
    }
}
