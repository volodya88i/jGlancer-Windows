using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Core.Scanning;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Stage-1 directory listing for Windows: one handle per directory and <c>GetFileInformationByHandleEx</c> with
/// <c>FileIdBothDirectoryInfo</c>, which returns every entry's attributes, size, times and NTFS file id in large
/// batches without opening the files (the file id is the identity hint the macOS app takes from the inode).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class WindowsDirectoryReader : IDirectoryReader
{
    private const int BufferSize = 64 * 1024;
    private const uint AttributeHidden = 0x2;
    private const uint AttributeSystem = 0x4;
    private const uint AttributeDirectory = 0x10;
    private const uint AttributeReparsePoint = 0x400;

    public IReadOnlyList<DirectoryEntry> Read(string directory)
    {
        using var handle = Win32.OpenForMetadata(directory, Win32.FileListDirectory);
        var entries = new List<DirectoryEntry>();
        var buffer = NativeMemory.AlignedAlloc(BufferSize, 8);
        try
        {
            var informationClass = Win32.FileIdBothDirectoryRestartInfo;
            while (true)
            {
                if (!Win32.GetFileInformationByHandleEx(handle, informationClass, buffer, BufferSize))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == Win32.ErrorNoMoreFiles)
                    {
                        break;
                    }
                    throw Win32.Exception(error, directory);
                }
                informationClass = Win32.FileIdBothDirectoryInfo;
                Parse((byte*)buffer, entries);
            }
        }
        finally
        {
            NativeMemory.AlignedFree(buffer);
        }
        return entries;
    }

    /// <summary>Walks one buffer of FILE_ID_BOTH_DIR_INFO records.</summary>
    private static void Parse(byte* buffer, List<DirectoryEntry> entries)
    {
        var entry = buffer;
        while (true)
        {
            var next = *(uint*)entry;
            var nameLength = *(uint*)(entry + 60) / 2;
            var name = new string((char*)(entry + 104), 0, (int)nameLength);
            if (name is not "." and not "..")
            {
                var attributes = *(uint*)(entry + 56);
                var isDirectory = (attributes & AttributeDirectory) != 0;
                entries.Add(new DirectoryEntry(
                    name,
                    isDirectory,
                    (attributes & (AttributeHidden | AttributeSystem)) != 0 || name.StartsWith('.'),
                    (attributes & AttributeReparsePoint) != 0,
                    isDirectory ? 0 : *(long*)(entry + 40),
                    FileTime(*(long*)(entry + 24)),
                    FileTime(*(long*)(entry + 8)),
                    *(ulong*)(entry + 96)));
            }
            if (next == 0)
            {
                return;
            }
            entry += next;
        }
    }

    private static DateTimeOffset FileTime(long value) => new(DateTime.FromFileTimeUtc(value), TimeSpan.Zero);

    /// <summary>The volume serial number as 8 hex digits.</summary>
    public string? VolumeId(string path)
    {
        try
        {
            using var handle = Win32.OpenForMetadata(path, Win32.FileReadAttributes);
            uint serial;
            return Win32.GetVolumeInformationByHandle(handle, null, 0, &serial, null, null, null, 0)
                ? serial.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The final path of a directory: junctions and links resolved, the case as stored on disk.</summary>
    public string Canonical(string path)
    {
        using var handle = Win32.OpenForMetadata(path, Win32.FileReadAttributes);
        var buffer = stackalloc char[1024];
        var length = Win32.GetFinalPathNameByHandle(handle, buffer, 1024, 0);
        string final;
        if (length is > 0 and < 1024)
        {
            final = new string(buffer, 0, (int)length);
        }
        else if (length >= 1024)
        {
            var large = new char[length + 1];
            fixed (char* pointer = large)
            {
                length = Win32.GetFinalPathNameByHandle(handle, pointer, (uint)large.Length, 0);
                final = new string(pointer, 0, (int)length);
            }
        }
        else
        {
            throw Win32.Exception(Marshal.GetLastPInvokeError(), path);
        }
        return Win32.ShortPath(final);
    }
}
