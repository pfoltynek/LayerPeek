#if DEBUG
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;

using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// The restricted DLL search of the app (T-052, finding L3), checked in the real app exe started as a child process: the
/// restriction is process-wide and cannot be undone, so it must never be applied to the test host. The Debug-only probe
/// switch makes the child load one library the way a <c>[DllImport]</c> does, first with the standard search and then
/// with the restricted one, and exit with the outcome; the app itself never starts.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NativeLibrarySearchTests : IDisposable
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A library with exactly one non-system dependency, which the dependency test renames.</summary>
    private const string DependentLibrary = "swscale-10.dll";

    private const string Dependency = "avutil-61.dll";

    private readonly string _directory = TestDirectories.NewDirectoryPath();

    public NativeLibrarySearchTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The DLL search order is a Windows loader feature.");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string AppExe => Path.Combine(AppContext.BaseDirectory, "LayerPeek.exe");

    public void Dispose() => TestDirectories.Delete(_directory);

    [Fact]
    public async Task LibraryFoundOnlyThroughPathIsNotLoaded()
    {
        SkipUnlessProbeAvailable();
        // Any loadable DLL under a name that exists nowhere else: the stand-in for a library missing next to the app.
        string pathDirectory = Directory.CreateDirectory(Path.Combine(_directory, "path")).FullName;
        string name = UniqueLibraryName();
        File.Copy(Path.Combine(AppContext.BaseDirectory, Dependency), Path.Combine(pathDirectory, name));

        NativeLibrarySearch.ProbeResult result = await ProbeAsync(name, pathDirectory);

        // The first flag proves the planted DLL is loadable through PATH at all, so the second one is meaningful.
        Assert.Equal(NativeLibrarySearch.ProbeResult.LoadedUnrestricted, result);
    }

    [Fact]
    public async Task DependencyFoundOnlyThroughPathIsNotLoaded()
    {
        SkipUnlessProbeAvailable();
        // A library loaded by its full path whose dependency is missing next to it but planted in a PATH folder: the case
        // of an FFmpeg DLL whose own dependency is missing. A copy of swscale gets its avutil import renamed to a unique
        // name of the same length, and avutil is planted under that name.
        string libraryDirectory = Directory.CreateDirectory(Path.Combine(_directory, "library")).FullName;
        string pathDirectory = Directory.CreateDirectory(Path.Combine(_directory, "path")).FullName;
        string dependencyName = UniqueLibraryName();
        string library = Path.Combine(libraryDirectory, UniqueLibraryName());
        byte[] image = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, DependentLibrary), Ct);
        Assert.SkipUnless(
            TryReplaceSingle(image, Encoding.ASCII.GetBytes(Dependency), Encoding.ASCII.GetBytes(dependencyName)),
            $"{DependentLibrary} does not name {Dependency} exactly once (another FFmpeg build?).");
        await File.WriteAllBytesAsync(library, image, Ct);
        File.Copy(Path.Combine(AppContext.BaseDirectory, Dependency), Path.Combine(pathDirectory, dependencyName));

        NativeLibrarySearch.ProbeResult result = await ProbeAsync(library, pathDirectory);

        Assert.Equal(NativeLibrarySearch.ProbeResult.LoadedUnrestricted, result);
    }

    [Fact]
    public async Task LibraryInTheApplicationFolderIsStillLoaded()
    {
        SkipUnlessProbeAvailable();
        // FFmpeg by name, as the bindings load it, with its dependencies next to it.
        NativeLibrarySearch.ProbeResult result = await ProbeAsync(Path.GetFileNameWithoutExtension(DependentLibrary), pathDirectory: null);

        Assert.Equal(NativeLibrarySearch.ProbeResult.LoadedUnrestricted | NativeLibrarySearch.ProbeResult.LoadedRestricted, result);
    }

    [Fact]
    public async Task SystemLibraryIsStillLoaded()
    {
        SkipUnlessProbeAvailable();
        // Not a known DLL (those are mapped from \KnownDlls regardless of the search path), so System32 is searched.
        NativeLibrarySearch.ProbeResult result = await ProbeAsync("credui", pathDirectory: null);

        Assert.Equal(NativeLibrarySearch.ProbeResult.LoadedUnrestricted | NativeLibrarySearch.ProbeResult.LoadedRestricted, result);
    }

    private static void SkipUnlessProbeAvailable()
    {
        // The probe switch exists only in Debug builds of the app.
        Assert.SkipUnless(
            typeof(NativeLibrarySearch).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration == "Debug",
            "Needs a Debug build of the app (test-only probe switch).");
        Assert.SkipUnless(File.Exists(AppExe), "The app exe is not next to the tests.");
        Assert.SkipUnless(
            File.Exists(Path.Combine(AppContext.BaseDirectory, DependentLibrary)) && File.Exists(Path.Combine(AppContext.BaseDirectory, Dependency)),
            "The FFmpeg libraries are not next to the tests (LayerPeekFFmpegEnabled=false?).");
    }

    /// <summary>Same length as <see cref="Dependency"/> (13 characters), so it can replace the import name in place.</summary>
    private static string UniqueLibraryName() => "p" + Guid.NewGuid().ToString("N")[..8] + ".dll";

    private static bool TryReplaceSingle(byte[] data, byte[] oldValue, byte[] newValue)
    {
        int first = data.AsSpan().IndexOf(oldValue);
        if (first < 0 || oldValue.Length != newValue.Length || data.AsSpan(first + 1).IndexOf(oldValue) >= 0)
        {
            return false;
        }

        newValue.CopyTo(data, first);
        return true;
    }

    /// <param name="pathDirectory">Put in front of the child's PATH; <see langword="null"/> keeps PATH as is.</param>
    private static async Task<NativeLibrarySearch.ProbeResult> ProbeAsync(string library, string? pathDirectory)
    {
        ProcessStartInfo start = new(AppExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // Never the folder of a planted DLL: the current directory must not be what finds it.
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.Environment[NativeLibrarySearch.ProbeVariable] = library;
        if (pathDirectory is not null)
        {
            start.Environment["PATH"] = pathDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        }

        using Process child = Process.Start(start) ?? throw new InvalidOperationException("The app did not start.");
        try
        {
            await child.WaitForExitAsync(Ct).WaitAsync(ExitTimeout, Ct);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                // Until it is gone the child still maps the planted DLLs, and Dispose could not delete them (which
                // would hide the real failure behind an IOException).
                child.WaitForExit(KillTimeout);
            }
        }

        int outcome = child.ExitCode - NativeLibrarySearch.ProbeExitCodeBase;
        Assert.True(
            outcome is >= 0 and <= (int)(NativeLibrarySearch.ProbeResult.LoadedUnrestricted | NativeLibrarySearch.ProbeResult.LoadedRestricted),
            $"Unexpected exit code {child.ExitCode} of the probe (restriction failed or the app started).");
        return (NativeLibrarySearch.ProbeResult)outcome;
    }
}
#endif
