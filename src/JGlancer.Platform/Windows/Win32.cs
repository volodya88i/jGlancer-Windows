using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace JGlancer.Platform.Windows;

/// <summary>The few Win32 calls the platform layer needs, source-generated (NativeAOT-safe).</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Win32
{
    public const uint FileListDirectory = 0x0001;
    public const uint FileReadAttributes = 0x0080;
    public const uint ShareAll = 0x1 | 0x2 | 0x4;
    public const uint OpenExisting = 3;
    public const uint FlagBackupSemantics = 0x02000000;
    public const int FileIdBothDirectoryInfo = 10;
    public const int FileIdBothDirectoryRestartInfo = 11;
    public const int ErrorNoMoreFiles = 18;
    public const int ErrorAccessDenied = 5;
    public const int ErrorFileNotFound = 2;
    public const int ErrorPathNotFound = 3;

    /// <summary>Removes a file or an empty folder at the next restart (a file in use; needs administrator rights, else does nothing).</summary>
    public static void DeleteAtRestart(string path) => MoveFileEx(path, null, 4);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileEx(string existing, string? replacement, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, void* buffer, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumeInformationByHandle(
        SafeFileHandle file, char* volumeName, uint volumeNameSize, uint* serialNumber, uint* maximumComponentLength,
        uint* fileSystemFlags, char* fileSystemName, uint fileSystemNameSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    public static partial uint GetFinalPathNameByHandle(SafeFileHandle file, char* path, uint size, uint flags);

    /// <summary>Opens a directory (or file) for metadata access; throws the matching .NET exception on failure.</summary>
    public static SafeFileHandle OpenForMetadata(string path, uint access)
    {
        var handle = CreateFile(LongPath(path), access, ShareAll, 0, OpenExisting, FlagBackupSemantics, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw Exception(error, path);
        }
        return handle;
    }

    /// <summary>The <c>\\?\</c> form of an absolute path, so Win32 accepts paths longer than 260 characters.</summary>
    public static string LongPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return full;
        }
        return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }

    /// <summary>Strips the <c>\\?\</c> prefix added by Win32 final-path calls.</summary>
    public static string ShortPath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
        {
            return @"\\" + path[8..];
        }
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    public static Exception Exception(int error, string path) => error switch
    {
        ErrorAccessDenied => new UnauthorizedAccessException($"access denied: {path}"),
        ErrorFileNotFound or ErrorPathNotFound => new DirectoryNotFoundException($"not found: {path}"),
        _ => new IOException($"{new Win32Exception(error).Message}: {path}", error),
    };
}
