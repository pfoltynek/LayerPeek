using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Support;

/// <summary>What the app runs on, for <see cref="SupportInfo"/>; <see cref="Current"/> reads it from the process.</summary>
/// <param name="AppVersion">Product version, the same as the About window shows.</param>
/// <param name="UiLanguage">The language of the texts (culture name).</param>
/// <param name="LanguageSetting">The saved language code, or <see langword="null"/> when the app follows the system.</param>
/// <param name="FFmpeg">FFmpeg version, or why it is not available (exception type only).</param>
internal sealed record SupportEnvironment(
    string AppVersion,
    string OperatingSystem,
    Architecture OsArchitecture,
    Architecture ProcessArchitecture,
    string Framework,
    bool NativeAot,
    string UiLanguage,
    string? LanguageSetting,
    string FFmpeg)
{
    /// <param name="appVersion">Product version (About window).</param>
    /// <param name="languageSetting">The saved language code, <see langword="null"/>: the system language.</param>
    /// <param name="ffmpegVersion">Reads the FFmpeg version; it may throw when FFmpeg cannot be loaded.</param>
    public static SupportEnvironment Current(string appVersion, string? languageSetting, Func<string> ffmpegVersion)
    {
        ArgumentNullException.ThrowIfNull(ffmpegVersion);
        string ffmpeg;
        try
        {
            ffmpeg = ffmpegVersion();
        }
        catch (Exception ex)
        {
            // Only the type: a native loader message may contain paths of the user's profile.
            ffmpeg = $"not available ({ex.GetType().Name})";
        }

        return new SupportEnvironment(
            appVersion,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture,
            RuntimeInformation.ProcessArchitecture,
            RuntimeInformation.FrameworkDescription,
            !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported,
            CultureInfo.CurrentUICulture.Name,
            languageSetting,
            ffmpeg);
    }
}

/// <summary>
/// The text for a problem report (T-048 <c>info.txt</c>, T-049 clipboard): app, system and the configured printers with
/// their current state. Always English and invariant (a developer reads it), never localized; lines end with <c>\n</c>.
/// Printer names, addresses and PrusaLink user names are included (decided 2026-10-21); camera addresses without
/// credentials and query; no secret ever reaches it (the settings hold none).
/// </summary>
internal static class SupportInfo
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Build(SupportEnvironment environment, AppSettings settings, IReadOnlyList<PrinterSnapshot> snapshots, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(snapshots);

        StringBuilder text = new();
        text.Append(Invariant, $"Prusa Tray Diagnostic {environment.AppVersion}").Append('\n');
        text.Append(Invariant, $"Created: {now:yyyy-MM-dd HH:mm:ss zzz}").Append('\n');
        text.Append(Invariant, $"OS: {environment.OperatingSystem} ({environment.OsArchitecture})").Append('\n');
        text.Append(Invariant, $"Process: {environment.ProcessArchitecture}, {environment.Framework}, Native AOT: {YesNo(environment.NativeAot)}").Append('\n');
        text.Append(Invariant, $"UI language: {environment.UiLanguage} (setting: {environment.LanguageSetting ?? "system"})").Append('\n');
        text.Append(Invariant, $"FFmpeg: {environment.FFmpeg}").Append('\n');
        text.Append(Invariant, $"Polling interval: {settings.PollingInterval.TotalSeconds:0.###} s, detailed logging: {OnOff(settings.DebugLogging)}").Append('\n');
        text.Append(Invariant, $"Printers: {settings.Printers.Count}").Append('\n');
        foreach (PrinterDefinition printer in settings.Printers)
        {
            AppendPrinter(text, printer, Find(snapshots, printer.Id), now);
        }

        return text.ToString();
    }

    private static void AppendPrinter(StringBuilder text, PrinterDefinition printer, PrinterSnapshot? snapshot, DateTimeOffset now)
    {
        string user = printer.ConnectionType == ConnectionType.PrusaLink ? $", user {printer.UserName}" : string.Empty;
        string camera = SettingsExport.SupportStreamUrl(printer.Camera) is { } address ? $"{OnOff(printer.Camera.Enabled)}, {address}" : "none";
        text.Append('\n');
        text.Append(Invariant, $"- {printer.Name} [{printer.Id}]").Append('\n');
        text.Append(Invariant, $"  Connection: {printer.ConnectionType}, {SettingsExport.SupportAddress(printer.Address)}{user}").Append('\n');
        text.Append(Invariant, $"  Model: {printer.DetectedModel?.ToString() ?? "not detected"}").Append('\n');
        text.Append(Invariant, $"  Camera: {camera}").Append('\n');
        if (snapshot is null)
        {
            text.Append("  State: no data yet\n");
            return;
        }

        string problem = snapshot.Problem == PrinterProblem.None ? string.Empty : $", problem {snapshot.Problem}";
        text.Append(Invariant, $"  State: {snapshot.State}{problem}, {(now - snapshot.Timestamp).TotalSeconds:0} s ago").Append('\n');
        if (!string.IsNullOrWhiteSpace(snapshot.StatusMessage))
        {
            text.Append(Invariant, $"  Message: {snapshot.StatusMessage.ReplaceLineEndings(" ")}").Append('\n');
        }
    }

    private static PrinterSnapshot? Find(IReadOnlyList<PrinterSnapshot> snapshots, string printerId)
    {
        foreach (PrinterSnapshot snapshot in snapshots)
        {
            if (string.Equals(snapshot.PrinterId, printerId, StringComparison.OrdinalIgnoreCase))
            {
                return snapshot;
            }
        }

        return null;
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string OnOff(bool value) => value ? "on" : "off";
}
