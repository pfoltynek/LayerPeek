using System.Text;

using LayerPeek.Core.Previews;
using LayerPeek.PrusaLink.Api;

namespace LayerPeek.PrusaLink;

/// <summary>
/// Preview links of a PrusaLink job (<c>file.refs.icon</c> / <c>file.refs.thumbnail</c> of <c>/api/v1/job</c>), stored
/// URL-escaped and relative (without the leading slash), so that they resolve below the printer's base address, including a
/// reverse-proxy path prefix.
/// </summary>
/// <param name="Icon">Escaped relative path of the small preview, or null.</param>
/// <param name="Thumbnail">Escaped relative path of the large preview, or null.</param>
internal sealed record PrusaLinkPreviewReference(string? Icon, string? Thumbnail) : PrintPreviewReference
{
    private const int MaxPathLength = 1024;

    /// <summary>The reference from the job's links; null when neither link is usable.</summary>
    public static PrusaLinkPreviewReference? FromRefs(JobFileRefs? refs)
    {
        string? icon = ToRelativePath(refs?.Icon);
        string? thumbnail = ToRelativePath(refs?.Thumbnail);
        return icon is null && thumbnail is null ? null : new PrusaLinkPreviewReference(icon, thumbnail);
    }

    public string? PathFor(PrintPreviewSize size) => size == PrintPreviewSize.Small ? Icon : Thumbnail;

    /// <summary>
    /// "/thumb/l/usb/BENCHY~1.BGC" → "thumb/l/usb/BENCHY~1.BGC". The firmware sends the path as is (it URL-decodes the
    /// request), so every segment is escaped here. Anything that is not a plain absolute path on the printer (query,
    /// fragment, backslash, dot segments, empty segments, control characters, "//host") is refused.
    /// </summary>
    internal static string? ToRelativePath(string? path)
    {
        if (path is null || path.Length is < 2 or > MaxPathLength || path[0] != '/')
        {
            return null;
        }

        StringBuilder relative = new(path.Length + 16);
        foreach (string segment in path[1..].Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.AsSpan().ContainsAny("?#\\") || !IsPlainText(segment))
            {
                return null;
            }

            if (relative.Length > 0)
            {
                relative.Append('/');
            }

            relative.Append(Uri.EscapeDataString(segment));
        }

        return relative.ToString();
    }

    /// <summary>No control characters and no lone surrogates (a JSON escape can produce one; escaping would turn it into U+FFFD).</summary>
    private static bool IsPlainText(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsControl(c) || char.IsLowSurrogate(c))
            {
                return false;
            }

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    return false;
                }

                i++;
            }
        }

        return true;
    }
}
