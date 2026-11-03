using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Core.Playback;

namespace JGlancer.Platform.Windows;

/// <summary>Formats Media Foundation encodes audio segments to.</summary>
public enum AudioEncoding
{
    Aac,
    Alac,
    Flac,

    /// <summary>MP3 at 320 kbit/s (Windows' MP3 encoder).</summary>
    Mp3,
}

/// <summary>
/// Segment export through Media Foundation (W-40), part of Windows: a video's streams copied without re-encoding into
/// MP4 from the keyframe at or before the start, and PCM encoded to AAC (256 kbit/s), ALAC or FLAC. Interfaces are
/// called through their vtables; slots, GUIDs and constants come from mfreadwrite.h, mfobjects.h, mfapi.h and
/// mfidl.h of the Windows SDK 10.0.26100.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class MediaFoundation
{
    private const uint Version = 0x0002_0070;
    private const uint AnyStream = 0xFFFFFFFE;
    private const uint EndOfStream = 0x2;
    private const int InvalidStream = unchecked((int)0xC00D36B3);
    private const long TicksPerSecond = 10_000_000;

    private static readonly Guid MajorType = new(0x48eba18e, 0xf8c9, 0x4687, 0xbf, 0x11, 0x0a, 0x74, 0xc9, 0xf9, 0x6a, 0x8f);
    private static readonly Guid SubType = new(0xf7e34c9a, 0x42e8, 0x4714, 0xb7, 0x4b, 0xcb, 0x29, 0xd7, 0x2c, 0x35, 0xe5);
    private static readonly Guid Channels = new(0x37e48bf5, 0x645e, 0x4c5b, 0x89, 0xde, 0xad, 0xa9, 0xe2, 0x9b, 0x69, 0x6a);
    private static readonly Guid SamplesPerSecond = new(0x5faeeae7, 0x0290, 0x4c31, 0x9e, 0x8a, 0xc5, 0x34, 0xf6, 0x8d, 0x9d, 0xba);
    private static readonly Guid BitsPerSample = new(0xf2deb57f, 0x40fa, 0x4764, 0xaa, 0x33, 0xed, 0x4f, 0x2d, 0x1f, 0xf6, 0x69);
    private static readonly Guid BlockAlignment = new(0x322de230, 0x9eeb, 0x43bd, 0xab, 0x7a, 0xff, 0x41, 0x22, 0x51, 0x54, 0x1d);
    private static readonly Guid AverageBytesPerSecond = new(0x1aab75c8, 0xcfef, 0x451c, 0xab, 0x95, 0xac, 0x03, 0x4b, 0x8e, 0x17, 0x31);
    private static readonly Guid AllSamplesIndependent = new(0xc9173739, 0x5e56, 0x461c, 0xb7, 0x13, 0x46, 0xfb, 0x99, 0x5c, 0xb9, 0x5f);
    private static readonly Guid Audio = new(0x73647561, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
    private static readonly Guid Video = new(0x73646976, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
    private static readonly Guid DisableThrottling = new(0x08b845d8, 0x2b74, 0x4afe, 0x9d, 0x53, 0xbe, 0x16, 0xd2, 0xd5, 0xae, 0x4f);
    private static readonly Guid ContainerType = new(0x150ff23f, 0x4abc, 0x478b, 0xac, 0x4f, 0xe1, 0x91, 0x6f, 0xba, 0x1c, 0xca);
    private static readonly Guid ContainerMpeg4 = new(0xdc6cd05d, 0xb9d0, 0x40ef, 0xbd, 0x35, 0xfa, 0x62, 0x2c, 0x1a, 0xb2, 0x8a);
    private static readonly Guid ContainerFlac = new(0x31344aa3, 0x05a9, 0x42b5, 0x90, 0x1b, 0x8e, 0x9d, 0x42, 0x57, 0xf7, 0x5e);
    private static readonly Guid ContainerMp3 = new(0xe438b912, 0x83f1, 0x4de6, 0x9e, 0x3a, 0x9f, 0xfb, 0xc6, 0xdd, 0x24, 0xd1);

    private static readonly Lock StartupLock = new();
    private static bool s_started;

    /// <summary>A media subtype GUID from its WAVE_FORMAT tag (DEFINE_MEDIATYPE_GUID).</summary>
    private static Guid FormatGuid(uint tag) => new(tag, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);

    private static readonly Guid Pcm = FormatGuid(0x0001);
    private static readonly Guid Float = FormatGuid(0x0003);

    private static Guid EncodingGuid(AudioEncoding encoding) => encoding switch
    {
        AudioEncoding.Aac => FormatGuid(0x1610),
        AudioEncoding.Alac => FormatGuid(0x6C61),
        AudioEncoding.Mp3 => FormatGuid(0x0055),
        _ => FormatGuid(0xF1AC),
    };

    private static void Start()
    {
        lock (StartupLock)
        {
            if (!s_started)
            {
                Check(MFStartup(Version, 0));
                s_started = true;
            }
        }
    }

    // MARK: Video

    /// <summary>
    /// Copies the audio and video of <paramref name="source"/> between <paramref name="start"/> and
    /// <paramref name="end"/> seconds into the MP4 file <paramref name="target"/>, without re-encoding: it starts at the
    /// keyframe at or before the start (players show those few frames), timestamps start at zero.
    /// </summary>
    public static void CopyVideoSegment(string source, string target, double start, double end)
    {
        Start();
        nint reader = 0, writer = 0, attributes = 0;
        try
        {
            Check(MFCreateSourceReaderFromURL(source, 0, &reader));
            Check(MFCreateAttributes(&attributes, 2));
            SetGuid(attributes, ContainerType, ContainerMpeg4);
            SetUInt32(attributes, DisableThrottling, 1);
            Check(MFCreateSinkWriterFromURL(target, 0, attributes, &writer));
            var outputs = new Dictionary<uint, uint>();
            uint? video = null;
            for (uint stream = 0; ; stream++)
            {
                nint type;
                var result = ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, int>)Slot(reader, 5))(reader, stream, 0, &type);
                if (result == InvalidStream)
                {
                    break;
                }
                Check(result);
                try
                {
                    Guid major;
                    Check(((delegate* unmanaged[Stdcall]<nint, Guid*, int>)Slot(type, 33))(type, &major));
                    var wanted = major == Audio || major == Video;
                    Check(((delegate* unmanaged[Stdcall]<nint, uint, int, int>)Slot(reader, 4))(reader, stream, wanted ? 1 : 0));
                    if (wanted)
                    {
                        uint output;
                        Check(((delegate* unmanaged[Stdcall]<nint, nint, uint*, int>)Slot(writer, 3))(writer, type, &output));
                        // The same type in and out: the sink writer copies, nothing is encoded.
                        Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)Slot(writer, 4))(writer, output, type, 0));
                        outputs[stream] = output;
                        if (major == Video)
                        {
                            video ??= stream;
                        }
                    }
                }
                finally
                {
                    Release(type);
                }
            }
            if (outputs.Count == 0)
            {
                throw new InvalidOperationException("no audio or video stream to copy");
            }
            // The video can only start at a keyframe, and the audio lands elsewhere after a seek. Everything is timed from
            // that keyframe; seeking to it again brings the audio to it as well.
            var origin = (long)(start * TicksPerSecond);
            SeekReader(reader, origin);
            if (video is { } first && FirstSampleTime(reader, first) is { } keyframe)
            {
                origin = keyframe;
                SeekReader(reader, origin);
            }
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(writer, 5))(writer));
            var endTicks = (long)(end * TicksPerSecond);
            var done = new HashSet<uint>();
            long? lastFrame = null;
            while (done.Count < outputs.Count)
            {
                uint actual, flags;
                long timestamp;
                nint sample;
                Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, uint*, uint*, long*, nint*, int>)Slot(reader, 9))(
                    reader, AnyStream, 0, &actual, &flags, &timestamp, &sample));
                try
                {
                    if ((flags & EndOfStream) != 0)
                    {
                        done.Add(actual);
                    }
                    if (sample == 0 || !outputs.TryGetValue(actual, out var output) || done.Contains(actual))
                    {
                        continue;
                    }
                    // Frames arrive in decoding order: those shown before a frame past the end may follow it and depend
                    // on it, so that frame is kept and the video stops at the next one shown after it.
                    var pastEnd = actual == video
                        ? lastFrame is { } last && timestamp > last
                        : timestamp >= endTicks;
                    if (pastEnd)
                    {
                        done.Add(actual);
                        Check(((delegate* unmanaged[Stdcall]<nint, uint, int, int>)Slot(reader, 4))(reader, actual, 0));
                        continue;
                    }
                    if (timestamp < origin)
                    {
                        continue;
                    }
                    if (actual == video && timestamp >= endTicks)
                    {
                        lastFrame = timestamp;
                    }
                    Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(sample, 36))(sample, timestamp - origin));
                    Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, int>)Slot(writer, 6))(writer, output, sample));
                }
                finally
                {
                    Release(sample);
                }
            }
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(writer, 11))(writer));
        }
        finally
        {
            Release(writer);
            Release(reader);
            Release(attributes);
        }
    }

    private static void SeekReader(nint reader, long ticks)
    {
        var position = new PropVariant64 { Type = 20, Value = ticks };
        var timeFormat = Guid.Empty;
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, PropVariant64*, int>)Slot(reader, 8))(reader, &timeFormat, &position));
    }

    /// <summary>The time of the next sample of <paramref name="stream"/>; other streams' samples on the way are dropped.</summary>
    private static long? FirstSampleTime(nint reader, uint stream)
    {
        while (true)
        {
            uint actual, flags;
            long timestamp;
            nint sample;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, uint*, uint*, long*, nint*, int>)Slot(reader, 9))(
                reader, AnyStream, 0, &actual, &flags, &timestamp, &sample));
            var hadSample = sample != 0;
            Release(sample);
            if (actual == stream && hadSample)
            {
                return timestamp;
            }
            if (actual == stream && (flags & EndOfStream) != 0)
            {
                return null;
            }
        }
    }

    // MARK: Audio

    /// <summary>
    /// Encodes <paramref name="audio"/> into <paramref name="target"/> (M4A for AAC and ALAC, FLAC, MP3). AAC and MP3
    /// take 16-bit samples at 44.1 or 48 kHz and at most two channels: the decode step prepares them so.
    /// </summary>
    public static void EncodeAudio(PcmAudio audio, string target, AudioEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(audio);
        Start();
        nint writer = 0, attributes = 0, output = 0, input = 0;
        try
        {
            Check(MFCreateAttributes(&attributes, 2));
            SetGuid(attributes, ContainerType, encoding switch
            {
                AudioEncoding.Flac => ContainerFlac,
                AudioEncoding.Mp3 => ContainerMp3,
                _ => ContainerMpeg4,
            });
            SetUInt32(attributes, DisableThrottling, 1);
            Check(MFCreateSinkWriterFromURL(target, 0, attributes, &writer));

            Check(MFCreateMediaType(&output));
            SetGuid(output, MajorType, Audio);
            SetGuid(output, SubType, EncodingGuid(encoding));
            SetUInt32(output, SamplesPerSecond, (uint)audio.SampleRate);
            SetUInt32(output, Channels, (uint)audio.Channels);
            if (encoding != AudioEncoding.Mp3)
            {
                // The MP3 encoder's output type has no sample depth (it refuses one).
                SetUInt32(output, BitsPerSample, (uint)(audio.IsFloat ? 32 : audio.BitsPerSample));
            }
            if (encoding == AudioEncoding.Aac)
            {
                // 256 kbit/s, the macOS setting (D-73).
                SetUInt32(output, AverageBytesPerSecond, 32_000);
            }
            else if (encoding == AudioEncoding.Mp3)
            {
                // 320 kbit/s, the highest MP3 rate.
                SetUInt32(output, AverageBytesPerSecond, 40_000);
            }

            Check(MFCreateMediaType(&input));
            SetGuid(input, MajorType, Audio);
            SetGuid(input, SubType, audio.IsFloat ? Float : Pcm);
            SetUInt32(input, SamplesPerSecond, (uint)audio.SampleRate);
            SetUInt32(input, Channels, (uint)audio.Channels);
            SetUInt32(input, BitsPerSample, (uint)audio.BitsPerSample);
            SetUInt32(input, BlockAlignment, (uint)audio.BlockAlign);
            SetUInt32(input, AverageBytesPerSecond, (uint)(audio.BlockAlign * audio.SampleRate));
            SetUInt32(input, AllSamplesIndependent, 1);

            uint stream;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, uint*, int>)Slot(writer, 3))(writer, output, &stream));
            Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)Slot(writer, 4))(writer, stream, input, 0));
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(writer, 5))(writer));

            // A tenth of a second per sample.
            var framesPerChunk = Math.Max(1, audio.SampleRate / 10);
            for (long frame = 0; frame < audio.Frames; frame += framesPerChunk)
            {
                var frames = (int)Math.Min(framesPerChunk, audio.Frames - frame);
                WriteChunk(writer, stream, audio, frame, frames);
            }
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(writer, 11))(writer));
        }
        finally
        {
            Release(input);
            Release(output);
            Release(writer);
            Release(attributes);
        }
    }

    private static void WriteChunk(nint writer, uint stream, PcmAudio audio, long frame, int frames)
    {
        var bytes = frames * audio.BlockAlign;
        nint buffer = 0, sample = 0;
        try
        {
            Check(MFCreateMemoryBuffer((uint)bytes, &buffer));
            byte* data;
            uint max, current;
            Check(((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &data, &max, &current));
            audio.Data.AsSpan((int)(frame * audio.BlockAlign), bytes).CopyTo(new Span<byte>(data, bytes));
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(buffer, 4))(buffer));
            Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(buffer, 6))(buffer, (uint)bytes));
            Check(MFCreateSample(&sample));
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(sample, 42))(sample, buffer));
            Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(sample, 36))(sample, frame * TicksPerSecond / audio.SampleRate));
            Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(sample, 38))(sample, (long)frames * TicksPerSecond / audio.SampleRate));
            Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, int>)Slot(writer, 6))(writer, stream, sample));
        }
        finally
        {
            Release(sample);
            Release(buffer);
        }
    }

    // MARK: Plumbing

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant64
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public long Value;
        public long Padding;
    }

    private static nint Slot(nint item, int index) => (*(nint**)item)[index];

    private static void Release(nint item)
    {
        if (item != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(item, 2))(item);
        }
    }

    private static void SetGuid(nint attributes, Guid key, Guid value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Slot(attributes, 24))(attributes, &key, &value));

    private static void SetUInt32(nint attributes, Guid key, uint value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, uint, int>)Slot(attributes, 21))(attributes, &key, value));

    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);

    [LibraryImport("mfplat.dll")]
    private static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateAttributes(nint* attributes, uint initialSize);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateMediaType(nint* type);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateSample(nint* sample);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateMemoryBuffer(uint maxLength, nint* buffer);

    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MFCreateSourceReaderFromURL(string url, nint attributes, nint* reader);

    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MFCreateSinkWriterFromURL(string url, nint byteStream, nint attributes, nint* writer);
}
