using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Moves a file to the Recycle Bin (never deletes it): a file replaced by Save As goes there first (D-74). Where the
/// Recycle Bin cannot take it (a network drive), Windows asks before deleting for good. The full file-operation
/// pipeline with undo comes in W7. Values and layout from shellapi.h (x64: natural packing).
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class RecycleBin
{
    private const uint Delete = 0x0003;
    private const ushort Silent = 0x0004;
    private const ushort NoConfirmation = 0x0010;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoErrorUi = 0x0400;
    private const ushort WantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileOperation
    {
        public nint Window;
        public uint Function;
        public char* From;
        public char* To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public nint NameMappings;
        public char* ProgressTitle;
    }

    /// <summary>True when <paramref name="path"/> is in the Recycle Bin (or was not there to begin with).</summary>
    public static bool Send(string path) => Send(path, out _);

    /// <summary>As <see cref="Send(string)"/>; <paramref name="error"/> is the shell's code (32: the file is in use).</summary>
    public static bool Send(string path, out int error)
    {
        error = 0;
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return true;
        }
        // A double-null-terminated list of one.
        var list = Path.GetFullPath(path) + "\0\0";
        fixed (char* from = list)
        {
            var operation = new FileOperation
            {
                Function = Delete,
                From = from,
                Flags = (ushort)(AllowUndo | NoConfirmation | Silent | NoErrorUi | WantNukeWarning),
            };
            error = SHFileOperationW(&operation);
            return error == 0 && operation.AnyOperationsAborted == 0 && !File.Exists(path) && !Directory.Exists(path);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHFileOperationW(FileOperation* operation);
}
