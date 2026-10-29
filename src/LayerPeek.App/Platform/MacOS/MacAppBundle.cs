namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// Where the running app is on macOS (T-019): inside its <c>LayerPeek.app</c> bundle (<c>…/LayerPeek.app/Contents/MacOS/LayerPeek</c>,
/// as the release script builds it) or not (a development build, the test host). Pure path checks plus one
/// <c>access</c> call; the decisions are testable without a bundle.
/// </summary>
internal static class MacAppBundle
{
    /// <summary>The executable inside the bundle (<c>CFBundleExecutable</c> of build/macos/Info.plist).</summary>
    public const string ExecutableName = "LayerPeek";

    /// <summary>Gatekeeper runs a quarantined app that was not moved from a read-only random path below this folder name.</summary>
    private const string TranslocationFolder = "/AppTranslocation/";

    /// <summary>
    /// The <c>.app</c> folder when <paramref name="executable"/> is <c>&lt;name&gt;.app/Contents/MacOS/LayerPeek</c>, otherwise
    /// <see langword="null"/> (also for a relative path).
    /// </summary>
    public static string? BundleOf(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !executable.StartsWith('/') || !string.Equals(Path.GetFileName(executable), ExecutableName, StringComparison.Ordinal))
        {
            return null;
        }

        string? macOS = Path.GetDirectoryName(executable);
        string? contents = macOS is null ? null : Path.GetDirectoryName(macOS);
        string? bundle = contents is null ? null : Path.GetDirectoryName(contents);
        return bundle is not null
            && string.Equals(Path.GetFileName(macOS), "MacOS", StringComparison.Ordinal)
            && string.Equals(Path.GetFileName(contents), "Contents", StringComparison.Ordinal)
            && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            ? bundle
            : null;
    }

    /// <summary>
    /// Whether a Login Item of <paramref name="bundle"/> would still open the app at the next login: not when Gatekeeper
    /// runs it translocated (a random read-only copy, gone after a restart) and not from a read-only volume (the mounted
    /// <c>.dmg</c>, gone after ejecting it). The user first moves the app, e.g. to the Applications folder.
    /// </summary>
    /// <param name="isReadOnly">Whether the folder lies on a read-only volume.</param>
    public static bool IsPermanentLocation(string bundle, Func<string, bool> isReadOnly)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundle);
        ArgumentNullException.ThrowIfNull(isReadOnly);
        return !bundle.Contains(TranslocationFolder, StringComparison.Ordinal) && !isReadOnly(bundle);
    }

    /// <summary>Whether <paramref name="folder"/> lies on a read-only volume (<c>EROFS</c>); a missing permission is not that.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    public static bool IsOnReadOnlyVolume(string folder) => MacUnixNative.TryAccessWritable(folder) == MacUnixNative.ReadOnlyFileSystem;
}
