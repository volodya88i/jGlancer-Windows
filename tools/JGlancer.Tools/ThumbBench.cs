using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using JGlancer.Platform.Windows;

namespace JGlancer.Tools;

/// <summary>
/// <c>thumbbench &lt;folder&gt; [n]</c>: for up to n photos of each type in a folder (read-only), the time of a 320-pixel
/// thumbnail through WIC (decode at size) and through the Windows thumbnail system (its cache when Explorer has one).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ThumbBench
{
    /// <summary>One file through each WIC interpolation mode.</summary>
    public static void Modes(string file)
    {
        foreach (var (mode, name) in new[] { (0, "nearest"), (1, "linear"), (2, "cubic"), (3, "fant"), (4, "high quality cubic") })
        {
            WicPhotoDecoder.Interpolation = mode;
            var clock = Stopwatch.StartNew();
            var image = WicPhotoDecoder.DecodeFile(file, 320, 320);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name,-20} {clock.Elapsed.TotalMilliseconds,8:0.0} ms {image?.Image.Width}x{image?.Image.Height}"));
        }
        WicPhotoDecoder.Interpolation = 3;
    }

    public static void Run(string folder, int count)
    {
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".heic" })
        {
            var files = Directory.EnumerateFiles(folder, "*" + extension, SearchOption.AllDirectories).Take(count).ToList();
            if (files.Count == 0)
            {
                continue;
            }
            double wic = 0, shell = 0, cached = 0, worst = 0;
            var worstFile = "";
            var cacheHits = 0;
            int wicHits = 0, shellHits = 0;
            foreach (var file in files)
            {
                var clock = Stopwatch.StartNew();
                if (WindowsThumbnailGenerator.Shell(file, 320, cacheOnly: true) is not null)
                {
                    cacheHits++;
                }
                cached += clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                if (extension is ".jpg" or ".png" or ".heic" && WindowsThumbnailGenerator.Photo(file, 320) is not null)
                {
                    wicHits++;
                }
                wic += clock.Elapsed.TotalMilliseconds;
                if (clock.Elapsed.TotalMilliseconds > worst)
                {
                    worst = clock.Elapsed.TotalMilliseconds;
                    worstFile = Path.GetFileName(file);
                }
                clock.Restart();
                if (WindowsThumbnailGenerator.Shell(file, 320) is not null)
                {
                    shellHits++;
                }
                shell += clock.Elapsed.TotalMilliseconds;
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{extension,-6} {files.Count,3} files | WIC {wic / files.Count,7:0.0} ms ({wicHits} ok) | Shell {shell / files.Count,7:0.0} ms ({shellHits} ok) | Windows cache {cached / files.Count,6:0.0} ms ({cacheHits} hits) | WIC worst {worst:0} ms {worstFile}"));
        }
    }
}
