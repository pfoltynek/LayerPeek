using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// Objective-C blocks without a bridge (T-020), Native AOT safe: a completion handler the app passes is a <em>global</em>
/// block (no captured state, <c>BLOCK_IS_GLOBAL</c>, isa <c>_NSConcreteGlobalBlock</c>) around an <c>UnmanagedCallersOnly</c>
/// function, allocated once and never freed; <c>Block_copy</c> and <c>Block_release</c> leave such a block alone. The
/// function finds its state in static fields of the caller. A block the system passes (the completion handler of a
/// delegate method) is called through its <c>invoke</c> pointer. Layout of the block ABI of clang (libclosure).
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class ObjCBlocks
{
    private const int BlockIsGlobal = 1 << 28;

    private static readonly Lazy<nint> GlobalBlockClass = new(static () =>
        NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteGlobalBlock"));

    /// <summary>A global block calling <paramref name="invoke"/> (its first argument is the block itself). Never freed.</summary>
    public static nint Global(nint invoke)
    {
        if (invoke == 0)
        {
            throw new ArgumentException("The block needs a function.", nameof(invoke));
        }

        BlockLiteral* block = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)(sizeof(BlockLiteral) + sizeof(BlockDescriptor)));
        BlockDescriptor* descriptor = (BlockDescriptor*)(block + 1);
        descriptor->Size = (nuint)sizeof(BlockLiteral);
        block->Isa = GlobalBlockClass.Value;
        block->Flags = BlockIsGlobal;
        block->Invoke = invoke;
        block->Descriptor = descriptor;
        return (nint)block;
    }

    /// <summary>Calls a block without arguments (<c>void (^)(void)</c>); 0 is ignored.</summary>
    public static void Invoke(nint block)
    {
        if (block != 0)
        {
            ((delegate* unmanaged<nint, void>)((BlockLiteral*)block)->Invoke)(block);
        }
    }

    /// <summary>Calls a block with one <c>NSUInteger</c> argument (<c>void (^)(UNNotificationPresentationOptions)</c>); 0 is ignored.</summary>
    public static void Invoke(nint block, nuint argument)
    {
        if (block != 0)
        {
            ((delegate* unmanaged<nint, nuint, void>)((BlockLiteral*)block)->Invoke)(block, argument);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public nint Isa;
        public int Flags;
        public int Reserved;
        public nint Invoke;
        public BlockDescriptor* Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }
}
