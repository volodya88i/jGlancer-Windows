using System.Runtime.Versioning;
using JGlancer.Core.Model;

namespace JGlancer.Platform.Windows;

/// <summary>The asynchronous entry (await is not allowed in the unsafe part).</summary>
[SupportedOSPlatform("windows")]
public static partial class MediaDetailsReader
{
    /// <summary>
    /// The details of <paramref name="path"/> off the calling thread: the Shell's first; video and audio it knows no
    /// codec of are asked of libmpv.
    /// </summary>
    public static async Task<MediaDetails> ReadAsync(string path, MediaKind kind, CancellationToken cancellation)
    {
        var details = await Task.Run(() => Read(path, kind), cancellation).ConfigureAwait(false);
        if (kind == MediaKind.Photo || details.VideoCodec is not null || details.AudioCodec is not null)
        {
            return details;
        }
        try
        {
            return await Mpv.MpvDetailsProbe.ReadAsync(path, cancellation).ConfigureAwait(false);
        }
        catch (Exception error) when (error is DllNotFoundException or InvalidOperationException)
        {
            // No libmpv (scripts\deps.ps1 not run): what the Shell knew.
            return details;
        }
    }
}
