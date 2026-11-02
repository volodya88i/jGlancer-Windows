using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>Files as Explorer handles them: Show in Explorer with the files selected, and a shell data object of them.</summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ShellFiles
{
    /// <summary>Explorer windows opened by one Show in Explorer at most (one per folder).</summary>
    public const int MaxFolders = 10;

    private static readonly Guid DataObjectHandler = new(0xb8c0bd9f, 0xed24, 0x455c, 0x83, 0xe6, 0xd5, 0x39, 0x0c, 0x4f, 0xe8, 0xc4);
    private static readonly Guid DataObjectInterface = new("0000010e-0000-0000-C000-000000000046");

    /// <summary>
    /// Opens the folders of <paramref name="paths"/> in Explorer with the files selected (Explorer's "Show in folder");
    /// one window per folder, at most <see cref="MaxFolders"/>.
    /// </summary>
    public static void ShowInExplorer(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var group in paths.GroupBy(p => Path.GetDirectoryName(p) ?? "", StringComparer.OrdinalIgnoreCase).Take(MaxFolders))
        {
            var folder = ILCreateFromPathW(group.Key);
            if (folder == 0)
            {
                continue;
            }
            var items = group.Select(ILCreateFromPathW).Where(item => item != 0).ToArray();
            try
            {
                var children = items.Select(item => ILFindLastID(item)).ToArray();
                fixed (nint* list = children)
                {
                    _ = SHOpenFolderAndSelectItems(folder, (uint)children.Length, list, 0);
                }
            }
            finally
            {
                foreach (var item in items)
                {
                    ILFree(item);
                }
                ILFree(folder);
            }
        }
    }

    /// <summary>A shell data object (IDataObject, AddRef'd: release it) of <paramref name="paths"/>, or 0.</summary>
    internal static nint DataObject(IReadOnlyList<string> paths)
    {
        var items = paths.Select(ILCreateFromPathW).Where(item => item != 0).ToArray();
        if (items.Length == 0)
        {
            return 0;
        }
        try
        {
            nint array;
            fixed (nint* list = items)
            {
                if (SHCreateShellItemArrayFromIDLists((uint)items.Length, list, &array) < 0 || array == 0)
                {
                    return 0;
                }
            }
            var shellItems = ComInterop.Wrap<IShellItemArray>(array);
            try
            {
                var handler = DataObjectHandler;
                var interfaceId = DataObjectInterface;
                nint dataObject;
                return shellItems.BindToHandler(0, &handler, &interfaceId, &dataObject) >= 0 ? dataObject : 0;
            }
            finally
            {
                ComInterop.Release(shellItems);
            }
        }
        finally
        {
            foreach (var item in items)
            {
                ILFree(item);
            }
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint ILCreateFromPathW(string path);

    [LibraryImport("shell32.dll")]
    private static partial nint ILFindLastID(nint pidl);

    [LibraryImport("shell32.dll")]
    private static partial void ILFree(nint pidl);

    [LibraryImport("shell32.dll")]
    private static partial int SHOpenFolderAndSelectItems(nint folder, uint count, nint* items, uint flags);

    [LibraryImport("shell32.dll")]
    private static partial int SHCreateShellItemArrayFromIDLists(uint count, nint* items, nint* array);
}
