using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Media.Thumbnails;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>
/// A program Windows offers for a file type (an association handler, as in Explorer's "Open with" menu). Holds its
/// COM object until disposed; UI thread (STA) only.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class ShellHandler : IDisposable
{
    private IAssocHandler? _handler;

    internal ShellHandler(IAssocHandler handler, string name, string uiName, string? iconPath, bool isRecommended)
    {
        _handler = handler;
        Name = name;
        DisplayName = uiName;
        IconPath = iconPath;
        IsRecommended = isRecommended;
    }

    /// <summary>The handler's own name: an executable's path for desktop programs, an app id for Store apps.</summary>
    public string Name { get; }

    public string DisplayName { get; }

    /// <summary>Where its icon is (an executable or a DLL), if Windows says.</summary>
    public string? IconPath { get; }

    public bool IsRecommended { get; }

    /// <summary>The program Windows opens this file type with.</summary>
    public bool IsDefault { get; internal set; }

    /// <summary>Opens <paramref name="paths"/> with this program, as Explorer does; false when Windows refused.</summary>
    public bool Open(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_handler is null || paths.Count == 0)
        {
            return false;
        }
        var dataObject = ShellFiles.DataObject(paths);
        if (dataObject == 0)
        {
            return false;
        }
        try
        {
            return _handler.Invoke(dataObject) >= 0;
        }
        finally
        {
            Marshal.Release(dataObject);
        }
    }

    public void Dispose()
    {
        if (_handler is { } handler)
        {
            _handler = null;
            ComInterop.Release(handler);
        }
    }
}

/// <summary>
/// Open With on Windows (FEATURES §9): the programs Windows recommends for a file type with its default first, the
/// system "Open with" dialog, and opening files with a chosen executable.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ShellOpenWith
{
    private const int FilterRecommended = 1;
    private const uint AssocStrExecutable = 2;
    private const uint AssocStrFriendlyAppName = 4;
    private const uint OaifExec = 0x4;
    private const uint OaifHideRegistration = 0x20;

    /// <summary>The programs Windows recommends for <paramref name="extension"/> (".jpg"), its default first. Dispose them.</summary>
    public static IReadOnlyList<ShellHandler> Recommended(string extension)
    {
        ArgumentException.ThrowIfNullOrEmpty(extension);
        var handlers = new List<ShellHandler>();
        nint pointer;
        if (SHAssocEnumHandlers(extension, FilterRecommended, &pointer) < 0 || pointer == 0)
        {
            return handlers;
        }
        var enumerator = ComInterop.Wrap<IEnumAssocHandlers>(pointer);
        try
        {
            nint item;
            uint fetched;
            while (enumerator.Next(1, &item, &fetched) == 0 && fetched == 1 && item != 0)
            {
                var handler = ComInterop.Wrap<IAssocHandler>(item);
                var name = Take(handler.GetName);
                var uiName = Take(handler.GetUIName);
                char* iconPath = null;
                int iconIndex;
                var icon = handler.GetIconLocation(&iconPath, &iconIndex) >= 0 && iconPath != null ? new string(iconPath) : null;
                if (iconPath != null)
                {
                    Marshal.FreeCoTaskMem((nint)iconPath);
                }
                if (name is null || uiName is null)
                {
                    ComInterop.Release(handler);
                    continue;
                }
                handlers.Add(new ShellHandler(handler, name, uiName, icon, handler.IsRecommended() == 0));
            }
        }
        finally
        {
            ComInterop.Release(enumerator);
        }
        // The default program first (by its executable, or its name for Store apps), the rest by name.
        var defaultExecutable = Query(AssocStrExecutable, extension);
        var defaultName = Query(AssocStrFriendlyAppName, extension);
        bool IsDefault(ShellHandler h) =>
            (defaultExecutable is not null && string.Equals(h.Name, defaultExecutable, StringComparison.OrdinalIgnoreCase))
            || (defaultName is not null && string.Equals(h.DisplayName, defaultName, StringComparison.OrdinalIgnoreCase));
        foreach (var handler in handlers)
        {
            handler.IsDefault = IsDefault(handler);
        }
        return [.. handlers.OrderBy(h => h.IsDefault ? 0 : 1).ThenBy(h => h.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Windows' own "Open with" dialog for one file; the chosen program opens it (nothing is registered).</summary>
    public static void ShowDialog(nint owner, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        fixed (char* file = path)
        {
            var info = new OpenAsInfo { File = file, Flags = OaifExec | OaifHideRegistration };
            _ = SHOpenWithDialog(owner, &info);
        }
    }

    /// <summary>Starts <paramref name="executable"/> with <paramref name="paths"/> as its arguments; false when it could not start.</summary>
    public static bool OpenWith(string executable, IReadOnlyList<string> paths)
    {
        ArgumentException.ThrowIfNullOrEmpty(executable);
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(paths.Count > 0 ? paths[0] : executable) ?? "" };
            foreach (var path in paths)
            {
                start.ArgumentList.Add(path);
            }
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>The program's name as Explorer shows it (its file description), else the file name.</summary>
    public static string ProgramName(string executable)
    {
        ArgumentException.ThrowIfNullOrEmpty(executable);
        try
        {
            var description = FileVersionInfo.GetVersionInfo(executable).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
        }
        catch (FileNotFoundException)
        {
        }
        return Path.GetFileNameWithoutExtension(executable);
    }

    /// <summary>A program's icon at <paramref name="size"/> pixels (an executable or the icon file Windows named), or null.</summary>
    public static ThumbnailImage? Icon(string path, int size) => File.Exists(path) ? WindowsThumbnailGenerator.ShellIcon(path, size) : null;

    private delegate int NameGetter(char** name);

    private static string? Take(NameGetter getter)
    {
        char* text = null;
        if (getter(&text) < 0 || text == null)
        {
            return null;
        }
        try
        {
            return new string(text);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)text);
        }
    }

    private static string? Query(uint what, string extension)
    {
        var size = 1024u;
        var buffer = stackalloc char[(int)size];
        return AssocQueryStringW(0, what, extension, null, buffer, &size) == 0 ? new string(buffer) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAsInfo
    {
        public char* File;
        public char* Class;
        public uint Flags;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHAssocEnumHandlers(string extension, int filter, nint* enumerator);

    [LibraryImport("shell32.dll")]
    private static partial int SHOpenWithDialog(nint owner, OpenAsInfo* info);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AssocQueryStringW(uint flags, uint what, string association, string? extra, char* output, uint* size);
}
