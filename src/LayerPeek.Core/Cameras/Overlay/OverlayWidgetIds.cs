namespace LayerPeek.Core.Cameras.Overlay;

/// <summary>
/// Stable text ids of the overlay widgets (T-077). An id is text, not an enum: a new widget is a new constant here plus
/// its class and one registration in the app; ids unknown to this version (written by a newer one) are kept in the layout
/// and saved back unchanged, they are only not drawn.
/// </summary>
public static class OverlayWidgetIds
{
    /// <summary>Printer state and progress (T-082).</summary>
    public const string Status = "status";

    /// <summary>Remaining time, finish time and print time (T-083).</summary>
    public const string Times = "times";

    /// <summary>Nozzle and bed temperatures with a sparkline (T-084).</summary>
    public const string Temperatures = "temperatures";

    /// <summary>Name of the printed file (T-085).</summary>
    public const string PrintName = "print-name";

    /// <summary>Cool-down after a print (T-088).</summary>
    public const string Cooldown = "cooldown";

    /// <summary>Longest id accepted (also for unknown ids read from the settings file).</summary>
    public const int MaxLength = 64;

    /// <summary>The widgets of this version, in the order of the settings table (T-076 8).</summary>
    public static IReadOnlyList<string> Known { get; } = [Status, Times, Temperatures, PrintName, Cooldown];

    /// <summary>Whether <paramref name="id"/> is exactly one of <see cref="Known"/>.</summary>
    public static bool IsKnown(string? id) => id is not null && Known.Contains(id, StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="id"/> can be a widget id: not empty, at most <see cref="MaxLength"/> characters, no white
    /// space at either end and no control characters.
    /// </summary>
    public static bool IsValid(string? id) =>
        !string.IsNullOrEmpty(id)
        && id.Length <= MaxLength
        && !char.IsWhiteSpace(id[0])
        && !char.IsWhiteSpace(id[^1])
        && !id.AsSpan().ContainsAnyInRange('\u0000', '\u001F')
        && !id.Contains('\u007F', StringComparison.Ordinal);

    /// <summary>The known id equal to <paramref name="id"/> ignoring case, or <paramref name="id"/> itself.</summary>
    internal static string Normalize(string id)
    {
        foreach (string known in Known)
        {
            if (string.Equals(known, id, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return id;
    }
}
