using System.Globalization;

namespace LayerPeek.App.Cameras.Layout;

/// <summary>Formats the texts of the layout mode in the current culture (the formats come from <c>Strings</c>).</summary>
internal static class CameraLayoutTexts
{
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
