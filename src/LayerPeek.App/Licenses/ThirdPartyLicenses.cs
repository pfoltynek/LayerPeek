using System.Text;

using LayerPeek.App.Resources;

namespace LayerPeek.App.Licenses;

/// <summary>A NuGet package covered by a <see cref="ThirdPartyComponent"/>.</summary>
/// <param name="Version">Exact version; <see langword="null"/> = whatever the SDK brings (the .NET runtime and shared frameworks).</param>
internal sealed record PackageRef(string Id, string? Version);

/// <summary>
/// A third-party component shipped with the app: its packages, licence (SPDX expression from the package metadata) and
/// licence texts (embedded resources, see <see cref="ThirdPartyLicenses.ReadText"/>).
/// </summary>
/// <param name="Version">Shown version; <see langword="null"/> for the .NET runtime and shared frameworks (read at run time).</param>
/// <param name="TextResources">Embedded resource names of the licence and notice texts, in display order.</param>
/// <param name="DisplayNameKey">
/// Key of the shown name in <c>Strings</c> when <paramref name="Name"/> needs a description (user decision 2026-10-19:
/// descriptions are translated, product names and licence texts are not); every translation contains
/// <paramref name="Name"/>. <see langword="null"/>: <paramref name="Name"/> is shown as it is.
/// </param>
internal sealed record ThirdPartyComponent(
    string Name,
    string? Version,
    string License,
    Uri ProjectUri,
    IReadOnlyList<PackageRef> Packages,
    IReadOnlyList<string> TextResources,
    string? DisplayNameKey = null);

/// <summary>
/// The single source of truth for licences of everything the app ships (T-014, user decision 2026-10-18). Every package
/// of the App's dependency graph (<c>project.assets.json</c>) must be listed here with its exact version; a test fails
/// otherwise, so a new or updated package cannot ship without its licence. Shared frameworks compiled into the app
/// (runtime packs, not packages) are listed by their runtime pack ids and checked against the framework references of
/// the projects; an entry for a package the app no longer uses fails a test as well. Licences were taken from the
/// package metadata (nuspec <c>license</c> expression or licence file), not guessed. FFmpeg itself (native, LGPL) is
/// described by <see cref="FFmpeg"/>.
/// </summary>
internal static class ThirdPartyLicenses
{
    private const string ResourcePrefix = "LayerPeek.Licenses.";

    /// <summary>Packages that are only used while building (compiler, trimmer, build telemetry); nothing of them ships.</summary>
    public static IReadOnlyList<string> BuildOnlyPackages { get; } =
    [
        "Microsoft.NET.ILLink.Tasks",
        "Avalonia.BuildServices",
    ];

    public static IReadOnlyList<ThirdPartyComponent> All { get; } =
    [
        new(
            "Avalonia",
            "12.1.4",
            "MIT",
            new Uri("https://github.com/AvaloniaUI/Avalonia"),
            [
                new("Avalonia", "12.1.4"),
                new("Avalonia.Desktop", "12.1.4"),
                new("Avalonia.Themes.Fluent", "12.1.4"),
                new("Avalonia.Skia", "12.1.4"),
                new("Avalonia.HarfBuzz", "12.1.4"),
                new("Avalonia.Win32", "12.1.4"),
                new("Avalonia.Native", "12.1.4"),
                new("Avalonia.X11", "12.1.4"),
                new("Avalonia.FreeDesktop", "12.1.4"),
                new("Avalonia.FreeDesktop.AtSpi", "12.1.4"),
                new("Avalonia.Remote.Protocol", "12.1.4"),
            ],
            [ResourcePrefix + "mit-avalonia.txt"]),
        new(
            "ANGLE (Avalonia.Angle.Windows.Natives, av_libglesv2.dll)",
            "2.1.27548.20260419",
            "BSD-3-Clause",
            new Uri("https://chromium.googlesource.com/angle/angle"),
            [new("Avalonia.Angle.Windows.Natives", "2.1.27548.20260419")],
            [ResourcePrefix + "angle.txt"]),
        new(
            "SkiaSharp + Skia (libSkiaSharp)",
            "3.119.4",
            "MIT",
            new Uri("https://github.com/mono/SkiaSharp"),
            [
                new("SkiaSharp", "3.119.4"),
                new("SkiaSharp.NativeAssets.Win32", "3.119.4"),
                new("SkiaSharp.NativeAssets.macOS", "3.119.4"),
                new("SkiaSharp.NativeAssets.Linux", "3.119.4"),
                new("SkiaSharp.NativeAssets.WebAssembly", "3.119.4"),
            ],
            [ResourcePrefix + "skiasharp.txt", ResourcePrefix + "skia-harfbuzz-native-notices.txt"]),
        new(
            "HarfBuzzSharp + HarfBuzz (libHarfBuzzSharp)",
            "8.3.1.3",
            "MIT",
            new Uri("https://github.com/mono/SkiaSharp"),
            [
                new("HarfBuzzSharp", "8.3.1.3"),
                new("HarfBuzzSharp.NativeAssets.Win32", "8.3.1.3"),
                new("HarfBuzzSharp.NativeAssets.macOS", "8.3.1.3"),
                new("HarfBuzzSharp.NativeAssets.Linux", "8.3.1.3"),
                new("HarfBuzzSharp.NativeAssets.WebAssembly", "8.3.1.3"),
            ],
            // The native notices are the same file as SkiaSharp's (Skia, HarfBuzz and their dependencies).
            [ResourcePrefix + "harfbuzzsharp.txt", ResourcePrefix + "skia-harfbuzz-native-notices.txt"]),
        new(
            "CommunityToolkit.Mvvm",
            "8.4.2",
            "MIT",
            new Uri("https://github.com/CommunityToolkit/dotnet"),
            [new("CommunityToolkit.Mvvm", "8.4.2")],
            [ResourcePrefix + "mit-communitytoolkit.txt"]),
        // The ASP.NET Core shared framework (FrameworkReference of LayerPeek.PrusaLink, T-028: the Kestrel web
        // proxy) also brings the Microsoft.Extensions libraries the app uses (DI, logging, HTTP). It is not a NuGet package
        // of the dependency graph but a runtime pack compiled into the executable; its version is that of the .NET runtime.
        new(
            "ASP.NET Core",
            null,
            "MIT",
            new Uri("https://github.com/dotnet/aspnetcore"),
            [
                new("Microsoft.AspNetCore.App.Runtime.win-x64", null),
                new("Microsoft.AspNetCore.App.Runtime.osx-arm64", null),
            ],
            [ResourcePrefix + "aspnetcore-license.txt", ResourcePrefix + "aspnetcore-third-party-notices.txt"],
            nameof(Strings.LicensesComponentAspNetCore)),
        new(
            ".NET",
            null,
            "MIT",
            new Uri("https://github.com/dotnet/dotnet"),
            [
                new("Microsoft.DotNet.ILCompiler", null),
                new("runtime.win-x64.Microsoft.DotNet.ILCompiler", null),
                new("runtime.osx-arm64.Microsoft.DotNet.ILCompiler", null),
            ],
            [ResourcePrefix + "mit-microsoft.txt", ResourcePrefix + "dotnet-third-party-notices.txt"],
            nameof(Strings.LicensesComponentDotNet)),
        new(
            "FFmpeg.AutoGen",
            "9.0.1.1",
            "MIT",
            new Uri("https://github.com/Ruslan-B/FFmpeg.AutoGen"),
            [new("FFmpeg.AutoGen.Abstractions", "9.0.1.1"), new("FFmpeg.AutoGen.Bindings.DynamicallyLinked", "9.0.1.1")],
            [ResourcePrefix + "ffmpeg-autogen.txt"]),
        new(
            "MicroCom.Runtime",
            "0.11.6",
            "MIT",
            new Uri("https://github.com/kekekeks/MicroCom"),
            [new("MicroCom.Runtime", "0.11.6")],
            [ResourcePrefix + "mit-microcom.txt"]),
        new(
            "Tmds.DBus.Protocol",
            "0.94.1",
            "MIT",
            new Uri("https://github.com/tmds/Tmds.DBus"),
            [new("Tmds.DBus.Protocol", "0.94.1")],
            [ResourcePrefix + "mit-tmds.txt"],
            nameof(Strings.LicensesComponentTmdsDBus)),
        // Installer hooks and updates (T-017); the same licence covers Setup.exe and Update.exe of the installer.
        new(
            "Velopack",
            "1.2.161",
            "MIT",
            new Uri("https://github.com/velopack/velopack"),
            [new("Velopack", "1.2.161")],
            [ResourcePrefix + "mit-velopack.txt"]),
    ];

    /// <summary>
    /// The licence texts of <paramref name="component"/>, separated by a line. Embedded resources are read by their exact
    /// names (no scanning), which is safe under Native AOT.
    /// </summary>
    /// <exception cref="InvalidOperationException">A resource is missing (a build problem, caught by the tests).</exception>
    public static string ReadText(ThirdPartyComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        StringBuilder text = new();
        foreach (string resource in component.TextResources)
        {
            if (text.Length > 0)
            {
                text.Append("\n\n").Append('-', 40).Append("\n\n");
            }

            text.Append(ReadResource(resource));
        }

        return text.ToString();
    }

    internal static string ReadResource(string name)
    {
        using Stream stream = typeof(ThirdPartyLicenses).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The licence text '{name}' is not embedded.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>FFmpeg (LGPL, dynamically linked native libraries next to the application).</summary>
    internal static class FFmpeg
    {
        public const string License = "LGPL-3.0-or-later";

        /// <summary>Licence text shipped next to the FFmpeg DLLs by <c>build/FFmpeg.targets</c>.</summary>
        public const string LicenseFileName = "FFmpeg-LICENSE.txt";

        public static Uri SourceUri { get; } = new("https://ffmpeg.org/download.html");

        /// <summary>The pinned BtbN build; must equal <c>LayerPeekFFmpegUrl</c> in <c>build/FFmpeg.targets</c> (checked by a test).</summary>
        public static Uri BuildUri { get; } =
            new("https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-17-20-51/ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0.zip");

        /// <summary>The configuration and build scripts of the BtbN builds (part of the corresponding source).</summary>
        public static Uri BuildScriptsUri { get; } = new("https://github.com/BtbN/FFmpeg-Builds");
    }
}
