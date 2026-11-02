using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace JGlancer.Setup;

/// <summary>
/// jGlancer-Setup-&lt;version&gt;.exe (W-73): this small program with the app's files appended as a zip archive. It
/// unpacks them into a temporary folder, runs <c>jGlancer.exe --setup</c> from there (the installer window is the app
/// itself, in its own look), waits for it and removes the folder. Arguments are passed on.
/// </summary>
internal static partial class Program
{
    /// <summary>The archive's end: its offset (8 bytes, little endian) and this mark (8 bytes).</summary>
    public static ReadOnlySpan<byte> Mark => "JGSETUP1"u8;

    public const int TrailerLength = 16;

    [STAThread]
    public static int Main(string[] args)
    {
        var folder = Path.Combine(Path.GetTempPath(), "jGlancer-Setup-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            if (!Unpack(Environment.ProcessPath!, folder))
            {
                Show("This setup file is damaged. Please download it again.");
                return 1;
            }
            var start = new ProcessStartInfo(Path.Combine(folder, "jGlancer.exe"))
            {
                UseShellExecute = false,
                WorkingDirectory = folder,
            };
            start.ArgumentList.Add("--setup");
            foreach (var argument in args)
            {
                start.ArgumentList.Add(argument);
            }
            using var setup = Process.Start(start);
            if (setup is null)
            {
                return 1;
            }
            setup.WaitForExit();
            return setup.ExitCode;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Show("jGlancer could not be set up: " + error.Message);
            return 1;
        }
        finally
        {
            Remove(folder);
        }
    }

    /// <summary>Unpacks the archive appended to <paramref name="setup"/>; false when there is none or it is broken.</summary>
    internal static bool Unpack(string setup, string folder)
    {
        using var file = File.OpenRead(setup);
        if (file.Length < TrailerLength)
        {
            return false;
        }
        Span<byte> trailer = stackalloc byte[TrailerLength];
        file.Seek(-TrailerLength, SeekOrigin.End);
        file.ReadExactly(trailer);
        if (!trailer[8..].SequenceEqual(Mark))
        {
            return false;
        }
        var offset = BitConverter.ToInt64(trailer[..8]);
        var length = file.Length - TrailerLength - offset;
        if (offset <= 0 || length <= 0)
        {
            return false;
        }
        using var archive = new ZipArchive(new Slice(file, offset, length), ZipArchiveMode.Read);
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
            // Only inside the folder.
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
        return File.Exists(Path.Combine(folder, "jGlancer.exe"));
    }

    private static void Remove(string folder)
    {
        for (var attempt = 0; attempt < 10 && Directory.Exists(folder); attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The setup window may take a moment to let go of its files.
                Thread.Sleep(300);
            }
        }
    }

    private static void Show(string message) => MessageBoxW(0, message, "jGlancer", 0x10);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint owner, string text, string caption, uint type);
}

/// <summary>A read-only window on part of a stream (the archive inside the setup file).</summary>
internal sealed class Slice(Stream inner, long start, long length) : Stream
{
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, length);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var left = (int)Math.Min(buffer.Length, length - _position);
        if (left <= 0)
        {
            return 0;
        }
        inner.Position = start + _position;
        var read = inner.Read(buffer[..left]);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => length + offset,
    };

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
