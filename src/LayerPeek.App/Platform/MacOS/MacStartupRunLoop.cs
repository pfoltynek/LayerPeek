using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The <c>CFRunLoop</c> of the thread that creates it (T-099: the main thread while macOS has no active display) with one
/// source of its own for <see cref="Wake"/>: <c>CFRunLoopRunInMode</c> in the default mode handles what the system
/// delivers on that run loop, for the main thread also the main dispatch queue, and returns once the source fired, so that
/// work posted to the main thread runs right away. A signalled source stays signalled until handled, so a wake before
/// <see cref="RunFor"/> is never lost (unlike <c>CFRunLoopStop</c>). Core Foundation by absolute path through
/// <c>[LibraryImport]</c>, blittable signatures, a static <c>[UnmanagedCallersOnly]</c> callback (Native AOT safe).
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacStartupRunLoop : IStartupRunLoop
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private readonly Lock _lock = new();
    private readonly nint _runLoop;
    private readonly nint _mode;
    private nint _source;

    private MacStartupRunLoop(nint runLoop, nint mode, nint source)
    {
        _runLoop = runLoop;
        _mode = mode;
        _source = source;
    }

    /// <summary>The run loop of the calling thread, or <see langword="null"/> when Core Foundation refused the source.</summary>
    public static MacStartupRunLoop? TryCreateForCurrentThread()
    {
        nint library = NativeLibrary.Load(CoreFoundation);
        // const CFRunLoopMode kCFRunLoopDefaultMode: a constant CFString of the framework, never released.
        nint mode = *(nint*)NativeLibrary.GetExport(library, "kCFRunLoopDefaultMode");
        // The run loop of a thread lives as long as the thread; not retained.
        nint runLoop = CFRunLoopGetCurrent();
        if (mode == 0 || runLoop == 0)
        {
            return null;
        }

        CFRunLoopSourceContext context = default;
        context.Perform = &Perform;
        // The context is copied by Core Foundation.
        nint source = CFRunLoopSourceCreate(0, 0, &context);
        if (source == 0)
        {
            return null;
        }

        CFRunLoopAddSource(runLoop, source, mode);
        return new MacStartupRunLoop(runLoop, mode, source);
    }

    public void RunFor(TimeSpan timeout)
    {
        lock (_lock)
        {
            if (_source == 0)
            {
                return;
            }
        }

        if (timeout > TimeSpan.Zero)
        {
            // Returns after the first source handled (our wake, or anything else), on timeout, or at once without sources.
            _ = CFRunLoopRunInMode(_mode, timeout.TotalSeconds, 1);
        }
    }

    public void Wake()
    {
        lock (_lock)
        {
            if (_source == 0)
            {
                return;
            }

            // Both thread-safe; the run loop object outlives this (its thread does).
            CFRunLoopSourceSignal(_source);
            CFRunLoopWakeUp(_runLoop);
        }
    }

    public void Dispose()
    {
        nint source;
        lock (_lock)
        {
            source = _source;
            _source = 0;
        }

        if (source != 0)
        {
            CFRunLoopSourceInvalidate(source);
            CFRelease(source);
        }
    }

    /// <summary>The source fired: nothing to do here, <c>CFRunLoopRunInMode</c> returns and the caller runs the posted work.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Perform(void* info)
    {
    }

    /// <summary><c>CFRunLoopSourceContext</c>, version 0.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CFRunLoopSourceContext
    {
        public nint Version;
        public void* Info;
        public void* Retain;
        public void* Release;
        public void* CopyDescription;
        public void* Equal;
        public void* Hash;
        public void* Schedule;
        public void* Cancel;
        public delegate* unmanaged[Cdecl]<void*, void> Perform;
    }

    [LibraryImport(CoreFoundation)]
    private static partial nint CFRunLoopGetCurrent();

    [LibraryImport(CoreFoundation)]
    private static partial nint CFRunLoopSourceCreate(nint allocator, nint order, CFRunLoopSourceContext* context);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRunLoopAddSource(nint runLoop, nint source, nint mode);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRunLoopSourceSignal(nint source);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRunLoopWakeUp(nint runLoop);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRunLoopSourceInvalidate(nint source);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);

    // SInt32 CFRunLoopRunInMode(CFRunLoopMode mode, CFTimeInterval seconds, Boolean returnAfterSourceHandled);
    [LibraryImport(CoreFoundation)]
    private static partial int CFRunLoopRunInMode(nint mode, double seconds, byte returnAfterSourceHandled);
}
