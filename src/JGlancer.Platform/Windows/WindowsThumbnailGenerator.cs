using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Core.Model;
using JGlancer.Media.Thumbnails;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Thumbnails on Windows (macOS ThumbnailGenerator): the Windows thumbnail cache first when it already has the file;
/// otherwise photos decoded at size through WIC (the scaler lets JPEG decode
/// at 1/2, 1/4, 1/8 resolution), EXIF orientation applied, never upscaled; video frames, audio cover art and
/// anything WIC cannot open from the Windows thumbnail system (thumbnails only: a file icon is not a thumbnail).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsThumbnailGenerator : IThumbnailGenerator
{
    private const int ThumbnailOnly = 0x8;
    private const int InCacheOnly = 0x10;

    /// <summary>False ignores the Windows thumbnail cache (benchmarks of a PC where Explorer never showed the folder).</summary>
    public static bool UsesWindowsCache { get; set; } = true;

    public ThumbnailImage? Generate(ThumbnailRequest request, int maxPixelSize, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            if (request.IsCloudOnly)
            {
                // Only what is on this PC already: reading the file would download it (MAC-040).
                return UsesWindowsCache ? Shell(request.Path, maxPixelSize, cacheOnly: true) : null;
            }
            // What Explorer already made costs a cache read, not a decode: 4–8 ms against 135 ms for a HEIC or a full
            // decode of a large PNG (W-37). Only a miss decodes. Explorer ignores a photo's color profile, so a photo
            // with a wide-gamut one (Display P3, Adobe RGB) is decoded here, in sRGB (MAC-036).
            // The cache holds the sizes Explorer showed, down to 48-pixel icons: a smaller one than asked for would look
            // blurred on the tile, so it is made again here instead.
            var wide = request.Kind == MediaKind.Photo && WicPhotoDecoder.HasWideProfile(request.Path);
            if (UsesWindowsCache && !wide && Shell(request.Path, maxPixelSize, cacheOnly: true) is { } cached
                && CoversRequest(cached.Width, cached.Height, maxPixelSize))
            {
                return cached;
            }
            if (request.Kind == MediaKind.Photo && Photo(request.Path, maxPixelSize) is { } photo)
            {
                return photo;
            }
            return cancellation.IsCancellationRequested ? null : Shell(request.Path, maxPixelSize);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a cached thumbnail is as large as the request on its longest side (a little short of it counts: Explorer
    /// rounds); smaller ones are never taken from Windows' cache.
    /// </summary>
    internal static bool CoversRequest(int width, int height, int maxPixelSize) => Math.Max(width, height) >= maxPixelSize * 0.9;

    /// <summary>A photo through WIC (the viewer's decoder), or null when WIC has no codec for it or the file is broken.</summary>
    internal static ThumbnailImage? Photo(string path, int maxPixelSize) => WicPhotoDecoder.DecodeFile(path, maxPixelSize, maxPixelSize)?.Image;

    /// <summary>The Windows thumbnail of any file (video frame, cover art, a codec's own thumbnail), or null.</summary>
    /// <param name="cacheOnly">Only what the Windows thumbnail cache already holds (no generation).</param>
    internal static ThumbnailImage? Shell(string path, int maxPixelSize, bool cacheOnly = false) =>
        ShellImage(path, maxPixelSize, ThumbnailOnly | (cacheOnly ? InCacheOnly : 0));

    /// <summary>The icon Explorer shows for a file (a program's own icon), or null.</summary>
    internal static ThumbnailImage? ShellIcon(string path, int size) => ShellImage(path, size, IconOnly);

    private const int IconOnly = 0x4;

    private static ThumbnailImage? ShellImage(string path, int maxPixelSize, int flags)
    {
        var interfaceId = typeof(IShellItemImageFactory).GUID;
        nint pointer;
        if (SHCreateItemFromParsingName(path, 0, &interfaceId, &pointer) < 0 || pointer == 0)
        {
            return null;
        }
        var factory = ComInterop.Wrap<IShellItemImageFactory>(pointer);
        nint bitmap = 0;
        try
        {
            if (factory.GetImage(new NativeSize { Width = maxPixelSize, Height = maxPixelSize }, flags, &bitmap) < 0 || bitmap == 0)
            {
                return null;
            }
            return Gdi.ReadBgra(bitmap) is { } read ? new ThumbnailImage(read.Width, read.Height, read.Pixels) : null;
        }
        finally
        {
            if (bitmap != 0)
            {
                Gdi.DeleteObject(bitmap);
            }
            ComInterop.Release(factory);
        }
    }


    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, Guid* interfaceId, nint* item);
}
