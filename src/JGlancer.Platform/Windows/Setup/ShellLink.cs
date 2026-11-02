using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows.Setup;

/// <summary>Windows shortcuts (.lnk) for the Start menu and the desktop (W-73), through the shell's own ShellLink object.</summary>
[SupportedOSPlatform("windows")]
public static partial class ShellLink
{
    // ShObjIdl_core.h: CLSID_ShellLink; IShellLinkW; ObjIdl.h: IPersistFile.
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");

    private const int AssociationsChanged = 0x08000000;
    private const uint IdList = 0;

    /// <summary>Explorer drops the icons it remembers: an update's new icon shows on the shortcuts without a restart.</summary>
    public static void NotifyIconsChanged() => SHChangeNotify(AssociationsChanged, IdList, 0, 0);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    /// <summary>Writes (or replaces) a shortcut at <paramref name="path"/> to <paramref name="target"/>.</summary>
    public static void Create(string path, string target, string description, string? arguments = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(target);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var link = ComInterop.Create<IShellLinkW>(ShellLinkClass, typeof(IShellLinkW).GUID);
        try
        {
            Marshal.ThrowExceptionForHR(link.SetPath(target));
            Marshal.ThrowExceptionForHR(link.SetWorkingDirectory(Path.GetDirectoryName(target) ?? ""));
            Marshal.ThrowExceptionForHR(link.SetDescription(description));
            Marshal.ThrowExceptionForHR(link.SetIconLocation(target, 0));
            if (arguments is not null)
            {
                Marshal.ThrowExceptionForHR(link.SetArguments(arguments));
            }
            var file = (IPersistFile)link;
            Marshal.ThrowExceptionForHR(file.Save(path, remember: 1));
        }
        finally
        {
            ComInterop.Release(link);
        }
    }

    /// <summary>The target of the shortcut at <paramref name="path"/> (tests, upgrades); null when it cannot be read.</summary>
    public static unsafe string? TargetOf(string path)
    {
        var link = ComInterop.Create<IShellLinkW>(ShellLinkClass, typeof(IShellLinkW).GUID);
        try
        {
            var file = (IPersistFile)link;
            if (file.Load(path, 0) < 0)
            {
                return null;
            }
            var buffer = stackalloc char[1024];
            return link.GetPath(buffer, 1024, 0, 0) < 0 ? null : new string(buffer);
        }
        finally
        {
            ComInterop.Release(link);
        }
    }
}

/// <summary>IShellLinkW (ShObjIdl_core.h), every method in vtable order up to SetPath.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IShellLinkW
{
    [PreserveSig]
    int GetPath(char* file, int length, nint findData, uint flags);

    [PreserveSig]
    int GetIDList(nint* list);

    [PreserveSig]
    int SetIDList(nint list);

    [PreserveSig]
    int GetDescription(char* name, int length);

    [PreserveSig]
    int SetDescription(string name);

    [PreserveSig]
    int GetWorkingDirectory(char* directory, int length);

    [PreserveSig]
    int SetWorkingDirectory(string directory);

    [PreserveSig]
    int GetArguments(char* arguments, int length);

    [PreserveSig]
    int SetArguments(string arguments);

    [PreserveSig]
    int GetHotkey(ushort* hotkey);

    [PreserveSig]
    int SetHotkey(ushort hotkey);

    [PreserveSig]
    int GetShowCmd(int* show);

    [PreserveSig]
    int SetShowCmd(int show);

    [PreserveSig]
    int GetIconLocation(char* path, int length, int* index);

    [PreserveSig]
    int SetIconLocation(string path, int index);

    [PreserveSig]
    int SetRelativePath(string path, uint reserved);

    [PreserveSig]
    int Resolve(nint window, uint flags);

    [PreserveSig]
    int SetPath(string file);
}

/// <summary>IPersistFile (ObjIdl.h) over IPersist.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010b-0000-0000-C000-000000000046")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IPersistFile
{
    [PreserveSig]
    int GetClassID(Guid* classId);

    [PreserveSig]
    int IsDirty();

    [PreserveSig]
    int Load(string fileName, uint mode);

    [PreserveSig]
    int Save(string fileName, int remember);

    [PreserveSig]
    int SaveCompleted(string fileName);

    [PreserveSig]
    int GetCurFile(char** fileName);
}
