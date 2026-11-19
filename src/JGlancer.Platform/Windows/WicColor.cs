using System.Runtime.Versioning;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Photos in sRGB (macOS MAC-036, owner 2026-11-13): a photo whose embedded ICC profile is not sRGB (every iPhone photo
/// is Display P3; Adobe RGB from cameras) is converted to sRGB on decode, in the thumbnails and in the viewer, so its
/// colors are not shown paler or duller than they are. Photos without a profile, or with an sRGB one, pass unchanged.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class WicColor
{
    private const int IccProfileType = 1;
    private const int ExifColorSpaceType = 2;
    private const uint ExifSrgb = 1;
    private static readonly Guid PixelFormatBgra = new("6fddc324-4e03-4bfe-b185-3d77768dc90f");

    /// <summary>
    /// The frame's color context when it is an ICC profile other than sRGB (added to <paramref name="owned"/>), else
    /// null: then the pixels are sRGB already or say nothing about their colors.
    /// </summary>
    public static IWICColorContext? WideProfile(IWICImagingFactory factory, IWICBitmapFrameDecode frame, List<object> owned)
    {
        nint pointer;
        if (factory.CreateColorContext(&pointer) < 0 || pointer == 0)
        {
            return null;
        }
        var context = ComInterop.Wrap<IWICColorContext>(pointer);
        owned.Add(context);
        uint count;
        int type;
        if (frame.GetColorContexts(1, &pointer, &count) < 0 || count == 0 || context.GetType(&type) < 0)
        {
            return null;
        }
        if (type == ExifColorSpaceType)
        {
            // EXIF says sRGB (1) or "uncalibrated" (0xFFFF), which has nothing to convert from.
            return null;
        }
        uint size;
        if (type != IccProfileType || context.GetProfileBytes(0, null, &size) < 0 || size == 0)
        {
            return null;
        }
        var bytes = new byte[size];
        fixed (byte* buffer = bytes)
        {
            if (context.GetProfileBytes(size, buffer, &size) < 0)
            {
                return null;
            }
        }
        return IsSrgb(IccProfile.Description(bytes)) ? null : context;
    }

    /// <summary>Whether a profile's description names sRGB (sRGB IEC61966-2.1, sRGB built-in, …).</summary>
    internal static bool IsSrgb(string? description) =>
        description is null || description.Contains("sRGB", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="source"/> converted from <paramref name="profile"/> to sRGB as 32-bit BGRA (added to
    /// <paramref name="owned"/>), or <paramref name="source"/> itself when WIC cannot convert it.
    /// </summary>
    public static IWICBitmapSource ToSrgb(IWICImagingFactory factory, IWICBitmapSource source, IWICColorContext profile, List<object> owned)
    {
        nint pointer;
        if (factory.CreateColorContext(&pointer) < 0 || pointer == 0)
        {
            return source;
        }
        var srgb = ComInterop.Wrap<IWICColorContext>(pointer);
        owned.Add(srgb);
        if (srgb.InitializeFromExifColorSpace(ExifSrgb) < 0 || factory.CreateColorTransformer(&pointer) < 0 || pointer == 0)
        {
            return source;
        }
        var transform = ComInterop.Wrap<IWICColorTransform>(pointer);
        owned.Add(transform);
        var format = PixelFormatBgra;
        return transform.Initialize(source, profile, srgb, &format) < 0 ? source : transform;
    }

    /// <summary>Whether the photo at <paramref name="path"/> has a profile to convert (a header read, no pixels).</summary>
    public static bool HasWideProfile(IWICImagingFactory factory, string path)
    {
        var owned = new List<object>();
        try
        {
            nint pointer;
            if (factory.CreateDecoderFromFilename(Win32.LongPath(path), null, 0x80000000, 0, &pointer) < 0 || pointer == 0)
            {
                return false;
            }
            var decoder = ComInterop.Wrap<IWICBitmapDecoder>(pointer);
            owned.Add(decoder);
            if (decoder.GetFrame(0, &pointer) < 0 || pointer == 0)
            {
                return false;
            }
            var frame = ComInterop.Wrap<IWICBitmapFrameDecode>(pointer);
            owned.Add(frame);
            return WideProfile(factory, frame, owned) is not null;
        }
        finally
        {
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                ComInterop.Release(owned[i]);
            }
        }
    }
}
