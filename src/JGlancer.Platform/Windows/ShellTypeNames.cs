using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows;

/// <summary>
/// The name Explorer gives a file type ("JPEG File", "MP4 Video File"), by extension, for the list view's Kind column
/// (macOS MAC-041: the type's own name). Asked once per extension; the file is never touched.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ShellTypeNames
{
    private const uint TypeNameFlag = 0x400; // SHGFI_TYPENAME
    private const uint UseAttributes = 0x10; // SHGFI_USEFILEATTRIBUTES
    private const uint NormalFile = 0x80; // FILE_ATTRIBUTE_NORMAL

    private static readonly ConcurrentDictionary<string, string?> Names = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        public fixed char DisplayName[260];
        public fixed char TypeName[80];
    }

    /// <summary>The type name of files with <paramref name="extension"/> (".jpg"); null when Windows has none.</summary>
    public static string? Of(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return Names.GetOrAdd(extension, static ext =>
        {
            FileInfo info;
            if (SHGetFileInfoW("file" + ext, NormalFile, &info, (uint)sizeof(FileInfo), TypeNameFlag | UseAttributes) == 0)
            {
                return null;
            }
            var name = new string(info.TypeName);
            return name.Length > 0 ? name : null;
        });
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SHGetFileInfoW(string path, uint attributes, FileInfo* info, uint size, uint flags);
}
