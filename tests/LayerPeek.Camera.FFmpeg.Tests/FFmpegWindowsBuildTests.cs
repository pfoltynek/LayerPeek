using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

using FFmpeg.AutoGen.Abstractions;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-202: the Windows libraries built from the official source by build/windows/build-ffmpeg.sh (MSYS2 + MinGW-w64) instead
/// of the BtbN build: the four libraries LayerPeek loads and nothing else (no swresample), each depending only on its
/// siblings and on DLLs of Windows (no DLL of the MinGW-w64 runtime), LGPL 3, and D3D11VA (hwcontext d3d11va with the H.264
/// hwaccel) as the only hardware decoder (user decision 2026-11-12, for T-206).
/// </summary>
public sealed class FFmpegWindowsBuildTests
{
    private const string WindowsOnly = "The Windows build of FFmpeg (build/windows/build-ffmpeg.sh); macOS: HardwareDecodingFallbackTests.";

    /// <summary>
    /// What the libraries may import: each other and DLLs that are part of every Windows (build-ffmpeg.sh, SYSTEM_DLLS of
    /// the RID): win-x64 the C runtime msvcrt.dll of MSYS2 MINGW64, win-arm64 (T-225, llvm-mingw ucrt) the API sets of the
    /// Universal C Runtime instead.
    /// </summary>
    private static readonly HashSet<string> AllowedImports = new(
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ?
            [
                "avformat-63.dll", "avcodec-63.dll", "avutil-61.dll", "swscale-10.dll",
                "kernel32.dll", "user32.dll", "advapi32.dll", "bcrypt.dll", "ws2_32.dll", "ole32.dll", "shell32.dll",
                "api-ms-win-crt-convert-l1-1-0.dll", "api-ms-win-crt-environment-l1-1-0.dll", "api-ms-win-crt-filesystem-l1-1-0.dll",
                "api-ms-win-crt-heap-l1-1-0.dll", "api-ms-win-crt-locale-l1-1-0.dll", "api-ms-win-crt-math-l1-1-0.dll",
                "api-ms-win-crt-private-l1-1-0.dll", "api-ms-win-crt-runtime-l1-1-0.dll", "api-ms-win-crt-stdio-l1-1-0.dll",
                "api-ms-win-crt-string-l1-1-0.dll", "api-ms-win-crt-time-l1-1-0.dll", "api-ms-win-crt-utility-l1-1-0.dll",
            ]
            :
            [
                "avformat-63.dll", "avcodec-63.dll", "avutil-61.dll", "swscale-10.dll",
                "kernel32.dll", "msvcrt.dll", "user32.dll", "advapi32.dll", "bcrypt.dll", "ws2_32.dll", "ole32.dll", "shell32.dll",
            ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>The libraries build/FFmpeg.targets provides on Windows (its _LayerPeekFFmpegLibrary items).</summary>
    private static readonly string[] Libraries = ["avcodec-63.dll", "avformat-63.dll", "avutil-61.dll", "swscale-10.dll"];

    [FFmpegFact]
    public void TheBuildProvidesTheFourLibrariesAndNothingLoadsSwresample()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        Assert.All(Libraries, static name => Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, name)), name));
        Assert.Contains("GNU LESSER GENERAL PUBLIC LICENSE", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "FFmpeg-LICENSE.txt")), StringComparison.Ordinal);
        // Loads all four (the version of each library). An output folder of an older build may still hold a
        // swresample-7.dll of the BtbN build (a build never deletes files): what counts is that nothing loads it.
        _ = FFmpegCameraFeedFactory.LibraryVersion;
        string[] loaded = [.. System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>()
            .Select(static module => module.FileName)
            .Where(static path => string.Equals(Path.GetDirectoryName(path)?.TrimEnd('\\'), AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            .Select(static path => Path.GetFileName(path).ToLowerInvariant())
            .Where(static name => System.Text.RegularExpressions.Regex.IsMatch(name, @"^(av|sw|postproc)[a-z]*-\d+\.dll$"))
            .Order(StringComparer.Ordinal)];

        Assert.Equal(Libraries, loaded);
    }

    [FFmpegFact]
    public void TheLibrariesDependOnlyOnEachOtherAndOnWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        foreach (string name in Libraries)
        {
            (Machine machine, List<string> imports) = ReadImports(Path.Combine(AppContext.BaseDirectory, name));

            Assert.Equal(RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? Machine.Arm64 : Machine.Amd64, machine);
            Assert.NotEmpty(imports);
            Assert.All(imports, imported => Assert.True(AllowedImports.Contains(imported), $"{name} imports {imported}."));
        }
    }

    [FFmpegFact]
    public void TheWindowsBuildHasD3D11VAForH264AsItsOnlyHardwareDecoderAndStaysLgpl()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        FFmpegRuntime.EnsureLoaded();

        // The hwcontext d3d11va (--enable-d3d11va) ...
        Assert.Equal(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, ffmpeg.av_hwdevice_find_type_by_name("d3d11va"));
        Assert.Equal([AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA], BuiltDeviceTypes());
        // ... with the H.264 hwaccel of that device (h264_d3d11va2, AV_PIX_FMT_D3D11) ...
        Assert.True(FFmpegRuntime.HasHardwareDecoder(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA));
        Assert.Contains(AVPixelFormat.AV_PIX_FMT_D3D11, H264HardwareFormats());
        // ... and the old D3D11VA API (h264_d3d11va, AV_PIX_FMT_D3D11VA_VLD); no DXVA2, D3D12VA or vendor decoders.
        Assert.Contains(AVPixelFormat.AV_PIX_FMT_D3D11VA_VLD, H264HardwareFormats());
        Assert.Equal(2, H264HardwareFormats().Count);
        Assert.False(FFmpegRuntime.HasHardwareDecoder(AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2));
        Assert.False(FFmpegRuntime.HasHardwareDecoder(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D12VA));
        Assert.False(FFmpegRuntime.HasHardwareDecoder(AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA));
        Assert.False(FFmpegRuntime.HasHardwareDecoder(AVHWDeviceType.AV_HWDEVICE_TYPE_QSV));
        Assert.EndsWith("(LGPL version 3 or later)", FFmpegCameraFeedFactory.LibraryVersion, StringComparison.Ordinal);
    }

    [FFmpegFact]
    public unsafe void TheConfigurationNamesNoExternalLibrary()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        FFmpegRuntime.EnsureLoaded();

        string configuration = ffmpeg.avcodec_configuration();

        Assert.Contains("--disable-autodetect", configuration, StringComparison.Ordinal);
        Assert.Contains("--disable-everything", configuration, StringComparison.Ordinal);
        Assert.Contains("--enable-version3", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-lib", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-gpl", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-nonfree", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-dxva2", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-d3d12va", configuration, StringComparison.Ordinal);
        // The decoder of the stream and nothing else.
        Assert.Equal(["h264"], Decoders());
    }

    private static List<AVHWDeviceType> BuiltDeviceTypes()
    {
        List<AVHWDeviceType> types = [];
        for (AVHWDeviceType type = ffmpeg.av_hwdevice_iterate_types(AVHWDeviceType.AV_HWDEVICE_TYPE_NONE);
            type != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            type = ffmpeg.av_hwdevice_iterate_types(type))
        {
            types.Add(type);
        }

        return types;
    }

    private static unsafe List<AVPixelFormat> H264HardwareFormats()
    {
        FFmpegRuntime.EnsureLoaded();
        AVCodec* decoder = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
        Assert.True(decoder is not null);
        List<AVPixelFormat> formats = [];
        for (int i = 0; ffmpeg.avcodec_get_hw_config(decoder, i) is var config && config is not null; i++)
        {
            formats.Add(config->pix_fmt);
        }

        return formats;
    }

    private static unsafe List<string> Decoders()
    {
        FFmpegRuntime.EnsureLoaded();
        List<string> names = [];
        void* opaque = null;
        for (AVCodec* codec = ffmpeg.av_codec_iterate(&opaque); codec is not null; codec = ffmpeg.av_codec_iterate(&opaque))
        {
            if (ffmpeg.av_codec_is_decoder(codec) != 0)
            {
                names.Add(Marshal.PtrToStringUTF8((IntPtr)codec->name) ?? string.Empty);
            }
        }

        return names;
    }

    /// <summary>The machine and the names of the imported DLLs of a PE file (its import directory, like objdump -p).</summary>
    private static (Machine Machine, List<string> Imports) ReadImports(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader reader = new(stream);
        PEHeaders headers = reader.PEHeaders;
        DirectoryEntry directory = headers.PEHeader!.ImportTableDirectory;
        PEMemoryBlock image = reader.GetEntireImage();
        byte[] bytes = image.GetContent().ToArray();

        int Offset(int rva)
        {
            foreach (SectionHeader section in headers.SectionHeaders)
            {
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData))
                {
                    return rva - section.VirtualAddress + section.PointerToRawData;
                }
            }

            throw new InvalidDataException($"RVA 0x{rva:x} of {path} is in no section.");
        }

        List<string> imports = [];
        // IMAGE_IMPORT_DESCRIPTOR: 20 bytes, the name RVA at offset 12; the table ends with a zeroed descriptor.
        for (int descriptor = Offset(directory.RelativeVirtualAddress); ; descriptor += 20)
        {
            int nameRva = BitConverter.ToInt32(bytes, descriptor + 12);
            if (nameRva == 0)
            {
                break;
            }

            int start = Offset(nameRva);
            int end = Array.IndexOf(bytes, (byte)0, start);
            imports.Add(Encoding.ASCII.GetString(bytes, start, end - start));
        }

        return (headers.CoffHeader.Machine, imports);
    }
}
