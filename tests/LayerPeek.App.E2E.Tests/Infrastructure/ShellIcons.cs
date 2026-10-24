using System.Drawing;
using System.Runtime.InteropServices;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The icon of a file as Explorer draws it, through the shell and its icon cache (T-070): <c>SHGetFileInfo</c> (the
/// system large and small icon) and <c>IShellItemImageFactory.GetImage</c> with <c>SIIGBF_ICONONLY</c> (the icon at any
/// size, what the views of Explorer use). Read-only; Explorer is not opened.
/// </summary>
internal static partial class ShellIcons
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;
    private const uint ShgfiSmallIcon = 0x1;
    private const int SiigbfIconOnly = 0x4;
    private const uint DibRgbColors = 0;

    /// <summary>The system large (<paramref name="large"/>) or small icon of <paramref name="file"/>, in straight ARGB.</summary>
    public static Pixels FileInfoIcon(string file, bool large)
    {
        ShFileInfo info = default;
        nint result = SHGetFileInfo(file, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | (large ? ShgfiLargeIcon : ShgfiSmallIcon));
        if (result == 0 || info.Icon == 0)
        {
            throw new InvalidOperationException($"SHGetFileInfo gave no {(large ? "large" : "small")} icon for '{file}'.");
        }

        try
        {
            using Icon icon = Icon.FromHandle(info.Icon);
            using Bitmap bitmap = icon.ToBitmap();
            return Pixels.From(bitmap);
        }
        finally
        {
            Native.DestroyIcon(info.Icon);
        }
    }

    /// <summary>The icon of <paramref name="file"/> at <paramref name="size"/> pixels from <c>IShellItemImageFactory</c>, in straight ARGB.</summary>
    public static Pixels ItemIcon(string file, int size)
    {
        Guid iid = typeof(IShellItemImageFactory).GUID;
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(file, 0, ref iid, out IShellItemImageFactory factory));
        try
        {
            Marshal.ThrowExceptionForHR(factory.GetImage(new NativeSize { Width = size, Height = size }, SiigbfIconOnly, out nint bitmap));
            try
            {
                return FromBitmapHandle(bitmap);
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(factory);
        }
    }

    /// <summary>
    /// The 32-bit pixels of a GDI bitmap, top-down. The shell's bitmaps carry premultiplied alpha; they are made straight
    /// (<see cref="IconPixels.LooksPremultiplied"/>). A bitmap without any alpha is taken as opaque.
    /// </summary>
    private static Pixels FromBitmapHandle(nint bitmap)
    {
        if (GetObject(bitmap, Marshal.SizeOf<BitmapHeader>(), out BitmapHeader header) == 0)
        {
            throw new InvalidOperationException("GetObject failed for the shell's bitmap.");
        }

        int width = header.Width;
        int height = Math.Abs(header.Height);
        Pixels pixels = new(width, height);
        BitmapInfoHeader request = new()
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            // Negative: top-down rows.
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };
        nint dc = GetDC(0);
        try
        {
            if (GetDIBits(dc, bitmap, 0, (uint)height, pixels.Argb, ref request, DibRgbColors) != height)
            {
                throw new InvalidOperationException("GetDIBits failed for the shell's bitmap.");
            }
        }
        finally
        {
            _ = ReleaseDC(0, dc);
        }

        if (pixels.Argb.All(static argb => (argb & unchecked((int)0xFF00_0000)) == 0))
        {
            for (int i = 0; i < pixels.Argb.Length; i++)
            {
                pixels.Argb[i] |= unchecked((int)0xFF00_0000);
            }

            return pixels;
        }

        return IconPixels.LooksPremultiplied(pixels) ? IconPixels.Unpremultiply(pixels) : pixels;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    /// <summary>BITMAP.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

    /// <summary>BITMAPINFOHEADER (no colour table follows for 32-bit BI_RGB).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out nint bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid, out IShellItemImageFactory item);

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static partial int GetObject(nint handle, int size, out BitmapHeader value);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] int[] bits, ref BitmapInfoHeader info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);
}
