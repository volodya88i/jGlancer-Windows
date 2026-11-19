using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Animated GIFs and PNGs (macOS MAC-039, ImageIO's CGAnimateImageAtURLWithBlock): the frames of a GIF with more than
/// one frame, or of a PNG with an animation control chunk and more than one frame (APNG), composed one after another
/// onto a canvas of the image's size as premultiplied BGRA, with each frame's own delay and the file's loop count.
/// GIF frames come from WIC; APNG frames are cut out of the file and decoded by WIC's PNG decoder one by one. A
/// multi-page TIFF or a multi-image HEIC is not an animation. Use from one thread at a time.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class AnimatedImageReader : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private static readonly Guid FactoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid PixelFormatPbgra = new("6fddc324-4e03-4bfe-b185-3d77768dc910");
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly IWICImagingFactory _factory;
    private readonly IWICBitmapDecoder? _gif;
    private readonly ApngFile? _png;
    private readonly byte[] _canvas;
    private byte[]? _saved;
    private Frame? _shown;

    private AnimatedImageReader(IWICImagingFactory factory, int width, int height, int frameCount, int loopCount, IWICBitmapDecoder? gif, ApngFile? png)
    {
        _factory = factory;
        Width = width;
        Height = height;
        FrameCount = frameCount;
        LoopCount = loopCount;
        _gif = gif;
        _png = png;
        _canvas = new byte[checked(width * height * 4)];
    }

    /// <summary>The canvas size in pixels.</summary>
    public int Width { get; }

    public int Height { get; }

    public int FrameCount { get; }

    /// <summary>How many times the animation plays; 0 for ever.</summary>
    public int LoopCount { get; }

    /// <summary>The frame the next <see cref="Next"/> composes (0 starts a new loop).</summary>
    public int Index { get; private set; }

    /// <summary>A frame's place on the canvas, how long it shows, and what happens to it before the next one.</summary>
    internal readonly record struct Frame(int X, int Y, int Width, int Height, TimeSpan Delay, int Disposal, bool Replaces);

    /// <summary>Whether <paramref name="path"/> is an animated GIF or PNG (a header read; no pixels are decoded).</summary>
    public static bool IsAnimated(string path)
    {
        try
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".png" or ".apng")
            {
                return IsAnimatedPng(path);
            }
            using var reader = extension == ".gif" ? Open(path) : null;
            return reader is not null;
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Opens an animated GIF or PNG; null for anything else (a still image, another format, a broken file).</summary>
    public static AnimatedImageReader? Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".gif" or ".png" or ".apng"))
        {
            return null;
        }
        var factory = ComInterop.Create<IWICImagingFactory>(FactoryClass, typeof(IWICImagingFactory).GUID);
        try
        {
            var reader = extension == ".gif" ? OpenGif(factory, path) : OpenPng(factory, path);
            if (reader is null)
            {
                ComInterop.Release(factory);
            }
            return reader;
        }
        catch
        {
            ComInterop.Release(factory);
            throw;
        }
    }

    /// <summary>
    /// Composes the next frame onto the canvas and copies the canvas into <paramref name="target"/> (Width × Height × 4
    /// bytes, premultiplied BGRA); returns how long that frame shows. After the last frame the next call starts over.
    /// </summary>
    public TimeSpan Next(Span<byte> target)
    {
        if (target.Length < _canvas.Length)
        {
            throw new ArgumentException("The target is smaller than the canvas.", nameof(target));
        }
        if (Index == 0)
        {
            Array.Clear(_canvas);
            _shown = null;
        }
        else if (_shown is { } previous)
        {
            Dispose(previous);
        }
        var (frame, pixels) = _gif is not null ? GifFrame(Index) : PngFrame(Index);
        if (frame.Disposal == 3)
        {
            _saved ??= new byte[_canvas.Length];
            _canvas.CopyTo(_saved, 0);
        }
        if (pixels is not null)
        {
            Compose(frame, pixels);
        }
        _shown = frame;
        Index = (Index + 1) % FrameCount;
        _canvas.CopyTo(target);
        return frame.Delay;
    }

    public void Dispose()
    {
        ComInterop.Release(_gif);
        ComInterop.Release(_factory);
    }

    // MARK: Composing

    /// <summary>What a frame leaves behind once it was shown: 2 clears its place, 3 brings back what was there.</summary>
    private void Dispose(Frame frame)
    {
        if (frame.Disposal is not (2 or 3))
        {
            return;
        }
        for (var y = Math.Max(0, frame.Y); y < Math.Min(Height, frame.Y + frame.Height); y++)
        {
            var start = (y * Width + Math.Max(0, frame.X)) * 4;
            var length = (Math.Min(Width, frame.X + frame.Width) - Math.Max(0, frame.X)) * 4;
            if (length <= 0)
            {
                continue;
            }
            if (frame.Disposal == 3 && _saved is not null)
            {
                Array.Copy(_saved, start, _canvas, start, length);
            }
            else
            {
                Array.Clear(_canvas, start, length);
            }
        }
    }

    /// <summary>Puts a frame's premultiplied pixels on the canvas: over what is there, or in its place.</summary>
    private void Compose(Frame frame, byte[] pixels)
    {
        for (var row = 0; row < frame.Height; row++)
        {
            var y = frame.Y + row;
            if (y < 0 || y >= Height)
            {
                continue;
            }
            for (var column = 0; column < frame.Width; column++)
            {
                var x = frame.X + column;
                if (x < 0 || x >= Width)
                {
                    continue;
                }
                var source = (row * frame.Width + column) * 4;
                var target = (y * Width + x) * 4;
                var alpha = pixels[source + 3];
                if (frame.Replaces || alpha == 255)
                {
                    Buffer.BlockCopy(pixels, source, _canvas, target, 4);
                }
                else if (alpha != 0)
                {
                    var rest = 255 - alpha;
                    for (var channel = 0; channel < 4; channel++)
                    {
                        _canvas[target + channel] = (byte)(pixels[source + channel] + (_canvas[target + channel] * rest + 127) / 255);
                    }
                }
            }
        }
    }

    /// <summary>A source's pixels as premultiplied BGRA, tightly packed.</summary>
    private byte[] Pixels(IWICBitmapSource source, int width, int height)
    {
        nint pointer;
        Marshal.ThrowExceptionForHR(_factory.CreateFormatConverter(&pointer));
        var converter = ComInterop.Wrap<IWICFormatConverter>(pointer);
        try
        {
            var format = PixelFormatPbgra;
            Marshal.ThrowExceptionForHR(converter.Initialize(source, &format, 0, 0, 0, 0));
            var pixels = new byte[checked(width * height * 4)];
            fixed (byte* buffer = pixels)
            {
                Marshal.ThrowExceptionForHR(converter.CopyPixels(0, (uint)width * 4, (uint)pixels.Length, buffer));
            }
            return pixels;
        }
        finally
        {
            ComInterop.Release(converter);
        }
    }

    /// <summary>Delays as browsers and ImageIO show them: very short ones (under 11 ms) as 100 ms.</summary>
    private static TimeSpan Delay(double seconds) => TimeSpan.FromSeconds(seconds < 0.011 ? 0.1 : seconds);

    // MARK: GIF

    private static AnimatedImageReader? OpenGif(IWICImagingFactory factory, string path)
    {
        nint pointer;
        if (factory.CreateDecoderFromFilename(Win32.LongPath(path), null, GenericRead, 0, &pointer) < 0 || pointer == 0)
        {
            return null;
        }
        var decoder = ComInterop.Wrap<IWICBitmapDecoder>(pointer);
        uint count;
        if (decoder.GetFrameCount(&count) < 0 || count < 2)
        {
            ComInterop.Release(decoder);
            return null;
        }
        var (width, height) = ScreenSize(decoder);
        if (width <= 0 || height <= 0)
        {
            ComInterop.Release(decoder);
            return null;
        }
        return new AnimatedImageReader(factory, width, height, (int)count, GifLoopCount(path), decoder, null);
    }

    /// <summary>The GIF's logical screen; the first frame's size when the file does not say.</summary>
    private static (int Width, int Height) ScreenSize(IWICBitmapDecoder decoder)
    {
        nint pointer;
        if (decoder.GetMetadataQueryReader(&pointer) >= 0 && pointer != 0)
        {
            var metadata = ComInterop.Wrap<IWICMetadataQueryReader>(pointer);
            try
            {
                var width = (int)(Integer(metadata, "/logscrdesc/Width") ?? 0);
                var height = (int)(Integer(metadata, "/logscrdesc/Height") ?? 0);
                if (width > 0 && height > 0)
                {
                    return (width, height);
                }
            }
            finally
            {
                ComInterop.Release(metadata);
            }
        }
        if (decoder.GetFrame(0, &pointer) < 0 || pointer == 0)
        {
            return (0, 0);
        }
        var frame = ComInterop.Wrap<IWICBitmapFrameDecode>(pointer);
        try
        {
            uint width;
            uint height;
            return frame.GetSize(&width, &height) < 0 ? (0, 0) : ((int)width, (int)height);
        }
        finally
        {
            ComInterop.Release(frame);
        }
    }

    /// <summary>The NETSCAPE2.0 application block's loop count (0: for ever); 1 when the file has none.</summary>
    internal static int GifLoopCount(string path)
    {
        var head = new byte[64 * 1024];
        int read;
        using (var file = File.OpenRead(path))
        {
            read = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }
        var marker = "NETSCAPE2.0"u8;
        var at = head.AsSpan(0, read).IndexOf(marker);
        if (at < 0 || at + marker.Length + 4 > read || head[at + marker.Length] != 3 || head[at + marker.Length + 1] != 1)
        {
            return 1;
        }
        return BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(at + marker.Length + 2));
    }

    private (Frame, byte[]?) GifFrame(int index)
    {
        nint pointer;
        Marshal.ThrowExceptionForHR(_gif!.GetFrame((uint)index, &pointer));
        var frame = ComInterop.Wrap<IWICBitmapFrameDecode>(pointer);
        IWICMetadataQueryReader? metadata = null;
        try
        {
            uint width;
            uint height;
            Marshal.ThrowExceptionForHR(frame.GetSize(&width, &height));
            long x = 0, y = 0, delay = 0, disposal = 0;
            if (frame.GetMetadataQueryReader(&pointer) >= 0 && pointer != 0)
            {
                metadata = ComInterop.Wrap<IWICMetadataQueryReader>(pointer);
                x = Integer(metadata, "/imgdesc/Left") ?? 0;
                y = Integer(metadata, "/imgdesc/Top") ?? 0;
                delay = Integer(metadata, "/grctlext/Delay") ?? 0;
                disposal = Integer(metadata, "/grctlext/Disposal") ?? 0;
            }
            var pixels = Pixels(frame, (int)width, (int)height);
            return (new Frame((int)x, (int)y, (int)width, (int)height, Delay(delay / 100.0), (int)disposal, Replaces: false), pixels);
        }
        finally
        {
            ComInterop.Release(metadata);
            ComInterop.Release(frame);
        }
    }

    private static long? Integer(IWICMetadataQueryReader metadata, string name)
    {
        var value = default(PropVariant);
        try
        {
            return metadata.GetMetadataByName(name, &value) >= 0 ? value.Integer : null;
        }
        finally
        {
            value.Clear();
        }
    }

    // MARK: APNG

    /// <summary>An APNG cut into its parts: the chunks every frame's PNG needs, and each frame's place and data.</summary>
    internal sealed record ApngFile(byte[] Header, List<byte[]> Ancillary, List<(Frame Frame, List<byte[]> Data)> Frames);

    private static AnimatedImageReader? OpenPng(IWICImagingFactory factory, string path)
    {
        if (ParseApng(File.ReadAllBytes(path)) is not { } png || png.File.Frames.Count < 2)
        {
            return null;
        }
        var width = BinaryPrimitives.ReadInt32BigEndian(png.File.Header);
        var height = BinaryPrimitives.ReadInt32BigEndian(png.File.Header.AsSpan(4));
        return width <= 0 || height <= 0 ? null : new AnimatedImageReader(factory, width, height, png.File.Frames.Count, png.Plays, null, png.File);
    }

    /// <summary>An animation control chunk with two frames or more before the image data: only chunk headers are read.</summary>
    private static bool IsAnimatedPng(string path)
    {
        using var file = File.OpenRead(path);
        Span<byte> head = stackalloc byte[8];
        if (file.ReadAtLeast(head, 8, throwOnEndOfStream: false) < 8 || !head.SequenceEqual(PngSignature))
        {
            return false;
        }
        Span<byte> chunk = stackalloc byte[16];
        while (file.ReadAtLeast(chunk[..8], 8, throwOnEndOfStream: false) == 8)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(chunk);
            if (length < 0 || chunk[4..8].SequenceEqual("IDAT"u8))
            {
                return false;
            }
            if (chunk[4..8].SequenceEqual("acTL"u8))
            {
                return length >= 8 && file.ReadAtLeast(chunk[8..16], 8, throwOnEndOfStream: false) == 8
                    && BinaryPrimitives.ReadUInt32BigEndian(chunk[8..]) > 1;
            }
            file.Seek(length + 4, SeekOrigin.Current);
        }
        return false;
    }

    /// <summary>The frames of an APNG and its play count; null for a PNG without an animation or a broken one.</summary>
    internal static (ApngFile File, int Plays)? ParseApng(byte[] bytes)
    {
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature))
        {
            return null;
        }
        byte[]? header = null;
        var plays = 0;
        var animated = false;
        var ancillary = new List<byte[]>();
        var frames = new List<(Frame, List<byte[]>)>();
        Frame? current = null;
        List<byte[]>? data = null;
        var seenData = false;
        for (var at = 8; at + 12 <= bytes.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at));
            if (length < 0 || at + 12 + length > bytes.Length)
            {
                return null;
            }
            var type = System.Text.Encoding.ASCII.GetString(bytes, at + 4, 4);
            var body = bytes.AsSpan(at + 8, length);
            switch (type)
            {
                case "IHDR":
                    header = body.ToArray();
                    break;
                case "acTL" when length >= 8:
                    animated = true;
                    plays = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(body[4..]));
                    break;
                case "fcTL" when length >= 26:
                    if (current is { } done && data is { Count: > 0 })
                    {
                        frames.Add((done, data));
                    }
                    var numerator = BinaryPrimitives.ReadUInt16BigEndian(body[20..]);
                    var denominator = BinaryPrimitives.ReadUInt16BigEndian(body[22..]);
                    // APNG's dispose ops (0 none, 1 background, 2 previous) map onto GIF's (1, 2, 3); a first frame
                    // cannot bring back what was before it, so "previous" clears it.
                    var dispose = body[24] switch { 1 => 2, 2 => frames.Count == 0 ? 2 : 3, _ => 1 };
                    current = new Frame(
                        (int)BinaryPrimitives.ReadUInt32BigEndian(body[12..]), (int)BinaryPrimitives.ReadUInt32BigEndian(body[16..]),
                        (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]), (int)BinaryPrimitives.ReadUInt32BigEndian(body[8..]),
                        Delay(numerator / (double)(denominator == 0 ? 100 : denominator)), dispose, Replaces: body[25] == 0);
                    data = [];
                    break;
                case "IDAT":
                    seenData = true;
                    // The default image is the first frame only when an fcTL came before it.
                    data?.Add(body.ToArray());
                    break;
                case "fdAT" when length >= 4:
                    data?.Add(body[4..].ToArray());
                    break;
                case "IEND":
                    break;
                default:
                    // PLTE, tRNS, gAMA, iCCP, sRGB …: every frame's PNG carries the ones from before the image data.
                    if (!seenData && type != "acTL" && type != "fcTL")
                    {
                        ancillary.Add(bytes.AsSpan(at, 12 + length).ToArray());
                    }
                    break;
            }
            at += 12 + length;
        }
        if (current is { } last && data is { Count: > 0 })
        {
            frames.Add((last, data));
        }
        return header is null || !animated ? null : (new ApngFile(header, ancillary, frames), plays);
    }

    private (Frame, byte[]?) PngFrame(int index)
    {
        var (frame, data) = _png!.Frames[index];
        var file = FramePng(_png, frame, data);
        nint stream;
        fixed (byte* buffer = file)
        {
            stream = SHCreateMemStream(buffer, (uint)file.Length);
        }
        if (stream == 0)
        {
            return (frame, null);
        }
        IWICBitmapDecoder? decoder = null;
        IWICBitmapFrameDecode? image = null;
        try
        {
            nint pointer;
            if (_factory.CreateDecoderFromStream(stream, null, 0, &pointer) < 0 || pointer == 0)
            {
                return (frame, null);
            }
            decoder = ComInterop.Wrap<IWICBitmapDecoder>(pointer);
            if (decoder.GetFrame(0, &pointer) < 0 || pointer == 0)
            {
                return (frame, null);
            }
            image = ComInterop.Wrap<IWICBitmapFrameDecode>(pointer);
            return (frame, Pixels(image, frame.Width, frame.Height));
        }
        finally
        {
            ComInterop.Release(image);
            ComInterop.Release(decoder);
            Marshal.Release(stream);
        }
    }

    /// <summary>One frame as a PNG of its own: the header with the frame's size, the shared chunks, its data, the end.</summary>
    private static byte[] FramePng(ApngFile png, Frame frame, List<byte[]> data)
    {
        using var output = new MemoryStream();
        output.Write(PngSignature);
        var header = (byte[])png.Header.Clone();
        BinaryPrimitives.WriteInt32BigEndian(header, frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), frame.Height);
        Chunk(output, "IHDR", header);
        foreach (var chunk in png.Ancillary)
        {
            output.Write(chunk);
        }
        foreach (var part in data)
        {
            Chunk(output, "IDAT", part);
        }
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] body)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, body.Length);
        output.Write(word);
        var name = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(name);
        output.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(name, body));
        output.Write(word);
    }

    private static readonly uint[] CrcTable = MakeCrcTable();

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] type, byte[] body)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        foreach (var b in body)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFF;
    }

    [LibraryImport("shlwapi.dll")]
    private static partial nint SHCreateMemStream(byte* data, uint size);
}
