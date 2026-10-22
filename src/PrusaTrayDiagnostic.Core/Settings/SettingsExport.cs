using System.Text.Json;

using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Settings.Storage;

namespace PrusaTrayDiagnostic.Core.Settings;

/// <summary>
/// The settings as <c>settings.json</c> for a diagnostic bundle (T-048): the same shape as the saved file, built again from
/// <see cref="AppSettings"/> (never a copy of the file). Settings hold no secrets (<see cref="Secrets.ISecretStore"/>);
/// camera addresses additionally lose their query and fragment, which may carry a token. Printer names, addresses and
/// PrusaLink user names stay (decided 2026-10-21).
/// </summary>
public static class SettingsExport
{
    public static string ToSupportJson(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SettingsDocument document = SettingsMapper.ToDocument(settings);
        for (int i = 0; i < settings.Printers.Count; i++)
        {
            if (document.Printers?[i] is not { } printer)
            {
                continue;
            }

            printer.Address = SupportAddress(settings.Printers[i].Address);
            if (printer.Camera is { } camera)
            {
                camera.StreamUrl = SupportStreamUrl(settings.Printers[i].Camera);
            }
        }

        return JsonSerializer.Serialize(document, SettingsJsonContext.Default.SettingsDocument);
    }

    /// <summary>
    /// A printer address as it is (host name, IP or URL), unless it is a URL with user info, query or fragment, which the
    /// settings editor never stores but a hand-edited file might: then those parts are masked (<see cref="UriRedaction"/>).
    /// </summary>
    public static string SupportAddress(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https"
            && (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || address.Contains('@', StringComparison.Ordinal))
            ? UriRedaction.Redact(uri)
            : address;
    }

    /// <summary>
    /// A camera address safe to hand out: scheme, host, port and path only, user info masked (<see cref="UriRedaction"/>).
    /// An invalid address kept as text for correction is not parsed at all: only its problem is given.
    /// </summary>
    public static string? SupportStreamUrl(CameraSettings camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (camera.StreamUri is { } uri)
        {
            return UriRedaction.Redact(new Uri(uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped)));
        }

        return camera.InvalidStreamUrl is null ? null : $"(invalid address hidden: {CameraSettings.Describe(camera.StreamUrlProblem)})";
    }
}
