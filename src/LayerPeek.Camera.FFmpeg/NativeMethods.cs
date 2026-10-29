using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen.Abstractions;

namespace LayerPeek.Camera.FFmpeg;

/// <summary>FFmpeg functions whose FFmpeg.AutoGen binding is not usable as is.</summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>
    /// <c>sws_scale</c> with raw pointers. The binding takes <c>byte*[]</c> arrays; under the JIT's runtime marshaller
    /// that crashes inside swscale (seen with FFmpeg.AutoGen 9.0.1.1 + FFmpeg 9.0, only Native AOT worked). A
    /// source-generated stub passes the pointers unchanged and needs no per-call arrays. On macOS the name is resolved to
    /// libswscale.10.dylib by <see cref="FFmpegRuntime"/>.
    /// </summary>
    [LibraryImport("swscale-10", EntryPoint = "sws_scale")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Scale(
        SwsContext* context,
        byte** sourceSlices,
        int* sourceStrides,
        int sliceY,
        int sliceHeight,
        byte** targetSlices,
        int* targetStrides);
}
