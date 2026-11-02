using System.Globalization;
using SkiaSharp;

namespace JGlancer.Tools;

/// <summary>
/// <c>synth-photos N</c>: N camera-sized JPEGs (4000 × 3000, 12 MP, quality 90) in %TEMP%\jGlancer-synthetic-photos-N,
/// one folder, for the viewer benchmark: gradients, bands and noise so they do not compress to nothing (~1.6 MB), every
/// one different. Synthetic, so no personal photos are needed for measurements.
/// </summary>
internal static class SyntheticPhotos
{
    private const int Width = 4000;
    private const int Height = 3000;

    public static void Create(int count)
    {
        var folder = Path.Combine(Path.GetTempPath(), string.Create(CultureInfo.InvariantCulture, $"jGlancer-synthetic-photos-{count}"));
        Directory.CreateDirectory(folder);
        var random = new Random(count);
        using var bitmap = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var started = DateTime.UtcNow;
        for (var index = 0; index < count; index++)
        {
            var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"IMG_{index + 1:0000}.jpg"));
            if (File.Exists(path))
            {
                continue;
            }
            Paint(bitmap, random);
            using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
            using var file = File.Create(path);
            data.SaveTo(file);
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{count} photos in {folder} ({(DateTime.UtcNow - started).TotalSeconds:0.0} s)"));
    }

    private static void Paint(SKBitmap bitmap, Random random)
    {
        using var canvas = new SKCanvas(bitmap);
        var hue = random.NextSingle() * 360;
        using (var gradient = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(Width, Height),
                [SKColor.FromHsv(hue, 60, 90), SKColor.FromHsv((hue + 120) % 360, 70, 40)], SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(0, 0, Width, Height, gradient);
        }
        using var shape = new SKPaint { IsAntialias = true };
        for (var i = 0; i < 40; i++)
        {
            shape.Color = SKColor.FromHsv(random.NextSingle() * 360, 50 + random.NextSingle() * 50, 30 + random.NextSingle() * 70).WithAlpha(160);
            canvas.DrawCircle(random.Next(Width), random.Next(Height), 50 + random.Next(600), shape);
        }
        // Fine grain over everything: what makes camera JPEGs megabytes, not kilobytes.
        using var grain = new SKPaint
        {
            Shader = SKShader.CreatePerlinNoiseFractalNoise(0.9f, 0.9f, 2, random.NextSingle() * 100),
            BlendMode = SKBlendMode.Overlay,
            Color = SKColors.White.WithAlpha(110),
        };
        canvas.DrawRect(0, 0, Width, Height, grain);
    }
}
