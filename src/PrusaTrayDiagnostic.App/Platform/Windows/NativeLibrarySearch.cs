using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>
/// Restricts where Windows looks for DLLs loaded by name, and for the dependencies of DLLs, to the application folder and
/// System32 (T-052, finding L3). Without it a library missing next to the app (an FFmpeg DLL or one of its dependencies)
/// would also be searched in the current directory and every PATH folder, where someone else may be able to write.
/// Libraries loaded by full path (the .NET runtime, shared framework, COM and WinRT servers) are not affected.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeLibrarySearch
{
    private const uint LoadLibrarySearchApplicationDir = 0x0000_0200;
    private const uint LoadLibrarySearchSystem32 = 0x0000_0800;

    /// <summary>The directories searched once <see cref="Restrict"/> succeeded.</summary>
    private const uint RestrictedDirectories = LoadLibrarySearchApplicationDir | LoadLibrarySearchSystem32;

    /// <summary>
    /// Applies the restriction to the whole process for its lifetime (it cannot be undone). Call first thing in
    /// <c>Main</c>, before anything loads a native library.
    /// </summary>
    /// <returns>0 on success, otherwise the Win32 error code (the process keeps the standard search order).</returns>
    public static int Restrict() =>
        // Exists on every Windows version .NET runs on (Windows 8 and later).
        SetDefaultDllDirectories(RestrictedDirectories) ? 0 : Marshal.GetLastPInvokeError();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restricting the DLL search to the application folder and System32 failed (error {ErrorCode}); the standard search order stays in effect.")]
    public static partial void LogRestrictionFailed(ILogger logger, int errorCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDefaultDllDirectories(uint directoryFlags);

#if DEBUG
    /// <summary>
    /// Debug builds only: the start loads the library named (or full path given) by this variable and exits with
    /// <see cref="ProbeExitCodeBase"/> + <see cref="ProbeResult"/> instead of starting the app (test of T-052).
    /// </summary>
    public const string ProbeVariable = "PRUSATRAYDIAGNOSTIC_NATIVE_LIBRARY_PROBE";

    public const int ProbeExitCodeBase = 40;

    /// <summary>Exit code of a probe whose <see cref="Restrict"/> failed.</summary>
    public const int ProbeRestrictionFailedExitCode = 39;

    [Flags]
    public enum ProbeResult
    {
        None = 0,
        LoadedUnrestricted = 1,
        LoadedRestricted = 2,
    }

    /// <summary>
    /// Loads <paramref name="library"/> like a <c>[DllImport]</c> of this assembly would, first with the standard search
    /// order (the control: is the library reachable at all?), unloads it, restricts the search and loads it again.
    /// Must run before anything else in the process applied the restriction.
    /// </summary>
    public static int RunProbe(string library)
    {
        ProbeResult result = ProbeResult.None;
        if (TryLoadAndFree(library))
        {
            result |= ProbeResult.LoadedUnrestricted;
        }

        if (Restrict() != 0)
        {
            return ProbeRestrictionFailedExitCode;
        }

        if (TryLoadAndFree(library))
        {
            result |= ProbeResult.LoadedRestricted;
        }

        return ProbeExitCodeBase + (int)result;
    }

    private static bool TryLoadAndFree(string library)
    {
        // Same resolution as [DllImport]: the assembly's folder by full path first, then the bare name (OS search).
        if (!NativeLibrary.TryLoad(library, typeof(NativeLibrarySearch).Assembly, searchPath: null, out nint handle))
        {
            return false;
        }

        NativeLibrary.Free(handle);
        return true;
    }
#endif
}
