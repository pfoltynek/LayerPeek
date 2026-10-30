namespace LayerPeek.App.Updates;

/// <summary>
/// The operating systems a release has files for (T-113). Both are released under one version, but not necessarily at
/// the same time: the files of one OS may be published first and the other OS added later, so a release counts for an OS
/// only once it has that OS's checksum file.
/// </summary>
internal enum ReleaseOs
{
    Windows,
    MacOS,
}

internal static class ReleaseOsExtensions
{
    /// <summary>The OS the app runs on; <see langword="null"/> on any other OS (no release has files for it).</summary>
    public static ReleaseOs? Current =>
        OperatingSystem.IsWindows() ? ReleaseOs.Windows
        : OperatingSystem.IsMacOS() ? ReleaseOs.MacOS
        : null;

    /// <summary>
    /// The checksum file the release scripts upload last for the OS (<c>scripts/release-upload.ps1</c>,
    /// <c>Get-ChecksumFileName</c>): its presence means the files of the OS are in the release.
    /// </summary>
    public static string ChecksumFileName(this ReleaseOs os) => os switch
    {
        ReleaseOs.Windows => "SHA256SUMS-windows.txt",
        ReleaseOs.MacOS => "SHA256SUMS-macos.txt",
        _ => throw new ArgumentOutOfRangeException(nameof(os), os, "Unknown operating system."),
    };
}
