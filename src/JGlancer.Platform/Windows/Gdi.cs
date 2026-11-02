using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows;

/// <summary>Reading the pixels of an HBITMAP (Shell thumbnails arrive as GDI bitmaps).</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Gdi
{
    /// <summary>
    /// The 32-bit pixels of <paramref name="bitmap"/>, top-down BGRA, and its size; null on failure. A bitmap whose alpha
    /// is all zero (no alpha channel in the source) is made opaque.
    /// </summary>
    public static (int Width, int Height, byte[] Pixels)? ReadBgra(nint bitmap)
    {
        BitmapInfo info;
        if (GetObjectW(bitmap, sizeof(BitmapInfo), &info) == 0 || info.Width <= 0 || info.Height == 0)
        {
            return null;
        }
        var width = info.Width;
        var height = Math.Abs(info.Height);
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height, // top-down rows
            Planes = 1,
            BitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var screen = GetDC(0);
        try
        {
            fixed (byte* buffer = pixels)
            {
                // BITMAPINFO = header + one RGBQUAD; with BI_RGB at 32 bpp the color table is unused.
                var withColors = stackalloc byte[sizeof(BitmapInfoHeader) + 4];
                *(BitmapInfoHeader*)withColors = header;
                if (GetDIBits(screen, bitmap, 0, (uint)height, buffer, withColors, 0) == 0)
                {
                    return null;
                }
            }
        }
        finally
        {
            _ = ReleaseDC(0, screen);
        }
        var anyAlpha = false;
        for (var i = 3; i < pixels.Length && !anyAlpha; i += 4)
        {
            anyAlpha = pixels[i] != 0;
        }
        if (!anyAlpha)
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }
        return (width, height, pixels);
    }

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint handle, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, void* bits, void* info, uint usage);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint dc);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

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
}
