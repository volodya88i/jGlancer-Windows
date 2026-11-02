using System.Runtime.InteropServices;

namespace JGlancer.Platform.Windows;

/// <summary>
/// A window's pixels as the screen shows them, GPU content included (PrintWindow with PW_RENDERFULLCONTENT), for
/// developer snapshots of video: an offscreen render of the visual tree cannot see an OpenGL surface.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static unsafe partial class WindowCapture
{
    // PW_CLIENTONLY | PW_RENDERFULLCONTENT: without the first the invisible resize border shifts the picture.
    private const uint RenderFullContent = 1 | 2;

    /// <summary>BGRA pixels of <paramref name="window"/>'s client area, or null.</summary>
    public static (int Width, int Height, byte[] Pixels)? Capture(nint window)
    {
        if (!GetClientRect(window, out var rect) || rect.Right <= 0 || rect.Bottom <= 0)
        {
            return null;
        }
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, rect.Right, rect.Bottom);
        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(window, memory, RenderFullContent))
            {
                return null;
            }
            SelectObject(memory, previous);
            return Gdi.ReadBgra(bitmap);
        }
        finally
        {
            Gdi.DeleteObject(bitmap);
            DeleteDC(memory);
            _ = ReleaseDC(0, screen);
        }
    }

    /// <summary>
    /// The screen's pixels where <paramref name="window"/> is (its visible frame), popups and menus over it included;
    /// the window must be on top. Null when it cannot be read.
    /// </summary>
    public static (int Width, int Height, byte[] Pixels)? CaptureScreenArea(nint window)
    {
        Rect frame;
        if (DwmGetWindowAttribute(window, ExtendedFrameBounds, &frame, (uint)sizeof(Rect)) < 0)
        {
            return null;
        }
        var width = frame.Right - frame.Left;
        var height = frame.Bottom - frame.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, frame.Left, frame.Top, SourceCopy))
            {
                return null;
            }
            SelectObject(memory, previous);
            return Gdi.ReadBgra(bitmap);
        }
        finally
        {
            Gdi.DeleteObject(bitmap);
            DeleteDC(memory);
            _ = ReleaseDC(0, screen);
        }
    }

    private const uint ExtendedFrameBounds = 9;
    private const uint SourceCopy = 0x00CC0020;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint window, uint attribute, void* value, uint size);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint window, nint dc, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleBitmap(nint dc, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint item);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}
