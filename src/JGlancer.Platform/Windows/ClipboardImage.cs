using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Media.Photos;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Puts a picture on the Windows clipboard (a saved video frame, W-75): its PNG bytes under the registered "PNG"
/// format (kept exactly; what Photoshop, Figma, browsers and chat apps paste) and a 32-bit device-independent bitmap
/// for everything else (Paint, Office).
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ClipboardImage
{
    private const uint CfDib = 8;
    private const uint GmemMoveable = 0x0002;

    /// <summary>Copies the PNG file at <paramref name="pngPath"/>; false when it cannot be read or the clipboard is busy.</summary>
    public static bool CopyPng(string pngPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(pngPath);
        byte[] png;
        DecodedPhoto? decoded;
        try
        {
            png = File.ReadAllBytes(pngPath);
            decoded = WicPhotoDecoder.DecodeFile(pngPath, IPhotoDecoder.Unbounded, IPhotoDecoder.Unbounded);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException)
        {
            return false;
        }
        if (decoded is null)
        {
            return false;
        }
        // Another program may hold the clipboard for a moment.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(0))
            {
                try
                {
                    EmptyClipboard();
                    var pngFormat = RegisterClipboardFormatW("PNG");
                    var placed = Place(pngFormat, png) | Place(CfDib, Dib(decoded));
                    return placed;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(20);
        }
        return false;
    }

    /// <summary>A bottom-up 32-bit BITMAPINFOHEADER bitmap of the decoded (opaque, so straight) pixels.</summary>
    private static byte[] Dib(DecodedPhoto decoded)
    {
        var image = decoded.Image;
        const int Header = 40;
        var dib = new byte[Header + image.Pixels.Length];
        fixed (byte* bytes = dib)
        {
            var header = (int*)bytes;
            header[0] = Header;
            header[1] = image.Width;
            header[2] = image.Height;
            *(short*)(bytes + 12) = 1;
            *(short*)(bytes + 14) = 32;
            header[4] = 0;
            header[5] = image.Pixels.Length;
        }
        for (var y = 0; y < image.Height; y++)
        {
            image.Pixels.AsSpan(y * image.Stride, image.Stride).CopyTo(dib.AsSpan(Header + (image.Height - 1 - y) * image.Stride));
        }
        return dib;
    }

    /// <summary>Hands <paramref name="data"/> to the clipboard (which then owns the memory).</summary>
    private static bool Place(uint format, byte[] data)
    {
        if (format == 0)
        {
            return false;
        }
        var memory = GlobalAlloc(GmemMoveable, (nuint)data.Length);
        if (memory == 0)
        {
            return false;
        }
        var target = GlobalLock(memory);
        if (target == 0)
        {
            GlobalFree(memory);
            return false;
        }
        Marshal.Copy(data, 0, target, data.Length);
        GlobalUnlock(memory);
        if (SetClipboardData(format, memory) == 0)
        {
            GlobalFree(memory);
            return false;
        }
        return true;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport("user32.dll")]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);
}
