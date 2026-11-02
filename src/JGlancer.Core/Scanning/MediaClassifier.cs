using System.Collections.Frozen;
using JGlancer.Core.Model;

namespace JGlancer.Core.Scanning;

/// <summary>
/// Classifies files into media kinds by extension only, as Explorer does: stage 1 never opens files (macOS D-30).
/// The table mirrors what macOS declares as image / movie / audio, plus the formats it indexes anyway (MKV, WebM,
/// Ogg, …) so the viewer can play them through libmpv or show "can't open — Open With…".
/// </summary>
public static class MediaClassifier
{
    /// <summary>A classified file type: its kind and a stable type identifier (macOS UTI where one exists).</summary>
    public sealed record Classification(MediaKind Kind, string TypeIdentifier);

    private static readonly FrozenDictionary<string, Classification> ByExtension = Build();

    /// <summary>The kind and type of a file name, or null if it is not photo, video or audio.</summary>
    public static Classification? Classify(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Length < 2 || Path.GetFileNameWithoutExtension(fileName).Length == 0)
        {
            return null;
        }
        return ByExtension.GetValueOrDefault(extension[1..].ToLowerInvariant());
    }

    /// <summary>Every extension the app opens (lowercase, no dot) with its classification (Explorer registration).</summary>
    public static IEnumerable<KeyValuePair<string, Classification>> All => ByExtension;

    private static FrozenDictionary<string, Classification> Build()
    {
        var table = new Dictionary<string, Classification>(StringComparer.Ordinal);
        void Add(MediaKind kind, string identifier, params string[] extensions)
        {
            foreach (var extension in extensions)
            {
                table[extension] = new Classification(kind, identifier);
            }
        }
        // Photos
        Add(MediaKind.Photo, "public.jpeg", "jpg", "jpeg", "jpe", "jfif");
        Add(MediaKind.Photo, "public.png", "png");
        Add(MediaKind.Photo, "com.compuserve.gif", "gif");
        Add(MediaKind.Photo, "com.microsoft.bmp", "bmp", "dib");
        Add(MediaKind.Photo, "public.tiff", "tif", "tiff");
        Add(MediaKind.Photo, "public.heic", "heic", "hif");
        Add(MediaKind.Photo, "public.heif", "heif");
        Add(MediaKind.Photo, "public.avif", "avif");
        Add(MediaKind.Photo, "org.webmproject.webp", "webp");
        Add(MediaKind.Photo, "public.jpeg-xl", "jxl");
        Add(MediaKind.Photo, "com.microsoft.jxr", "jxr", "wdp", "hdp");
        Add(MediaKind.Photo, "com.adobe.photoshop-image", "psd");
        Add(MediaKind.Photo, "com.adobe.raw-image", "dng");
        Add(MediaKind.Photo, "public.camera-raw-image",
            "cr2", "cr3", "crw", "nef", "nrw", "arw", "srf", "sr2", "orf", "rw2", "raf", "pef", "ptx", "srw", "x3f",
            "3fr", "erf", "kdc", "dcr", "mos", "mef", "mrw", "rwl", "iiq");
        // Video
        Add(MediaKind.Video, "com.apple.quicktime-movie", "mov", "qt");
        Add(MediaKind.Video, "public.mpeg-4", "mp4");
        Add(MediaKind.Video, "com.apple.m4v-video", "m4v");
        Add(MediaKind.Video, "public.3gpp", "3gp", "3g2");
        Add(MediaKind.Video, "public.avi", "avi");
        Add(MediaKind.Video, "org.matroska.mkv", "mkv", "mk3d");
        Add(MediaKind.Video, "org.webmproject.webm", "webm");
        Add(MediaKind.Video, "com.microsoft.windows-media-wmv", "wmv", "asf");
        Add(MediaKind.Video, "com.adobe.flash.video", "flv", "f4v");
        Add(MediaKind.Video, "org.xiph.ogv", "ogv");
        Add(MediaKind.Video, "public.mpeg", "mpg", "mpeg", "mpe", "m2v", "vob");
        Add(MediaKind.Video, "public.mpeg-2-transport-stream", "ts", "mts", "m2ts");
        Add(MediaKind.Video, "public.dv-movie", "dv");
        Add(MediaKind.Video, "org.smpte.mxf", "mxf");
        // Audio
        Add(MediaKind.Audio, "public.mp3", "mp3");
        Add(MediaKind.Audio, "com.apple.m4a-audio", "m4a", "m4b");
        Add(MediaKind.Audio, "public.aac-audio", "aac");
        Add(MediaKind.Audio, "com.microsoft.waveform-audio", "wav", "wave");
        Add(MediaKind.Audio, "public.aiff-audio", "aif", "aiff", "aifc");
        Add(MediaKind.Audio, "org.xiph.flac", "flac");
        Add(MediaKind.Audio, "org.xiph.ogg-audio", "ogg", "oga");
        Add(MediaKind.Audio, "org.xiph.opus", "opus");
        Add(MediaKind.Audio, "com.microsoft.windows-media-wma", "wma");
        Add(MediaKind.Audio, "com.monkeysaudio.ape", "ape");
        Add(MediaKind.Audio, "com.wavpack.wv", "wv");
        Add(MediaKind.Audio, "org.matroska.mka", "mka");
        Add(MediaKind.Audio, "com.apple.coreaudio-format", "caf");
        Add(MediaKind.Audio, "public.ac3-audio", "ac3", "eac3");
        Add(MediaKind.Audio, "public.dts-audio", "dts");
        Add(MediaKind.Audio, "org.3gpp.adaptive-multi-rate-audio", "amr");
        return table.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
