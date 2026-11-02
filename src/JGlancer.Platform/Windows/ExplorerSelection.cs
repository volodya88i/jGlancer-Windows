using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>
/// The files selected in an Explorer window or on the desktop (WINDOWS_PORT: IShellWindows → IShellBrowser →
/// IFolderView2.GetSelection). Explorer's tabs share one top-level window: the visible tab is the one read. Items
/// without a file-system path (the Recycle Bin, inside a zip) are left out. Call it on an STA thread (the UI thread).
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ExplorerSelection
{
    private static readonly Guid ShellWindowsClass = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid TopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private const uint ClsCtxAll = 0x17;
    private const ushort VariantInt = 3;
    private const int DesktopWindowClass = 8;
    private const int NeedDispatch = 1;
    private const uint FileSystemPath = 0x80058000;

    /// <summary>
    /// The selected paths of the Explorer window <paramref name="window"/> (top level), or of the desktop when it is
    /// the desktop's; empty when nothing (with a path) is selected or the window is not Explorer's.
    /// </summary>
    public static IReadOnlyList<string> Of(nint window, bool isDesktop)
    {
        IShellWindows? windows = null;
        try
        {
            windows = Create<IShellWindows>(ShellWindowsClass);
            if (isDesktop)
            {
                var empty = default(PropVariant);
                int hwnd;
                nint dispatch;
                return windows.FindWindowSW(&empty, &empty, DesktopWindowClass, &hwnd, NeedDispatch, &dispatch) >= 0 && dispatch != 0
                    ? FromDispatch(dispatch, tab: null) ?? []
                    : [];
            }
            int count;
            if (windows.GetCount(&count) < 0)
            {
                return [];
            }
            var tab = VisibleTab(window);
            for (var i = 0; i < count; i++)
            {
                var index = new PropVariant { Type = VariantInt, Int64 = i };
                nint dispatch;
                if (windows.Item(index, &dispatch) < 0 || dispatch == 0)
                {
                    continue;
                }
                if (FromDispatch(dispatch, tab ?? window) is { } paths)
                {
                    return paths;
                }
            }
            return [];
        }
        catch (Exception error) when (error is COMException or InvalidCastException)
        {
            return [];
        }
        finally
        {
            ComInterop.Release(windows);
        }
    }

    /// <summary>The selection of one Shell window, when it is <paramref name="tab"/> (null: any).</summary>
    private static List<string>? FromDispatch(nint dispatch, nint? tab)
    {
        IServiceProvider? provider = null;
        IShellBrowser? browser = null;
        IFolderView2? view = null;
        IShellItemArray? items = null;
        try
        {
            provider = ComInterop.Wrap<IServiceProvider>(dispatch);
            var service = TopLevelBrowser;
            var browserId = typeof(IShellBrowser).GUID;
            nint pointer;
            if (provider.QueryService(&service, &browserId, &pointer) < 0 || pointer == 0)
            {
                return null;
            }
            browser = ComInterop.Wrap<IShellBrowser>(pointer);
            nint browserWindow;
            if (tab is { } wanted && (browser.GetWindow(&browserWindow) < 0 || !SameWindow(browserWindow, wanted)))
            {
                return null;
            }
            if (browser.QueryActiveShellView(&pointer) < 0 || pointer == 0)
            {
                return null;
            }
            view = ComInterop.Wrap<IFolderView2>(pointer);
            if (view.GetSelection(0, &pointer) < 0 || pointer == 0)
            {
                return [];
            }
            items = ComInterop.Wrap<IShellItemArray>(pointer);
            uint count;
            if (items.GetCount(&count) < 0)
            {
                return [];
            }
            var paths = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                if (items.GetItemAt(i, &pointer) < 0 || pointer == 0)
                {
                    continue;
                }
                var item = ComInterop.Wrap<IShellItem>(pointer);
                try
                {
                    char* name;
                    if (item.GetDisplayName(FileSystemPath, &name) >= 0 && name != null)
                    {
                        paths.Add(new string(name));
                        Marshal.FreeCoTaskMem((nint)name);
                    }
                }
                finally
                {
                    ComInterop.Release(item);
                }
            }
            return paths;
        }
        catch (Exception error) when (error is COMException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            ComInterop.Release(items);
            ComInterop.Release(view);
            ComInterop.Release(browser);
            ComInterop.Release(provider);
        }
    }

    /// <summary>The browser's window is the tab itself (Windows 11) or its top-level window (one tab).</summary>
    private static bool SameWindow(nint browserWindow, nint wanted) =>
        browserWindow == wanted || GetAncestor(browserWindow, RootAncestor) == wanted && GetAncestor(wanted, RootAncestor) == wanted && !HasTabs(wanted);

    /// <summary>Windows 11 Explorer: the visible tab ("ShellTabWindowClass") of a top-level window; null without tabs.</summary>
    private static nint? VisibleTab(nint window)
    {
        nint child = 0;
        while ((child = FindWindowEx(window, child, "ShellTabWindowClass", null)) != 0)
        {
            if (IsWindowVisible(child))
            {
                return child;
            }
        }
        return null;
    }

    private static bool HasTabs(nint window) => FindWindowEx(window, 0, "ShellTabWindowClass", null) != 0;

    private const uint RootAncestor = 2;

    private static T Create<T>(Guid classId)
        where T : class
    {
        var interfaceId = typeof(T).GUID;
        nint pointer;
        Marshal.ThrowExceptionForHR(CoCreateInstance(&classId, 0, ClsCtxAll, &interfaceId, &pointer));
        return ComInterop.Wrap<T>(pointer);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* classId, nint outer, uint context, Guid* interfaceId, nint* instance);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint after, string? className, string? title);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint window, uint flags);
}

/// <summary>ExDisp.h IShellWindows (dual: the four IDispatch slots first, unused here).</summary>
[GeneratedComInterface]
[Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IShellWindows
{
    [PreserveSig] int GetTypeInfoCount(nint count);
    [PreserveSig] int GetTypeInfo(nint index, nint locale, nint info);
    [PreserveSig] int GetIDsOfNames(nint iid, nint names, nint count, nint locale, nint ids);
    [PreserveSig] int Invoke(nint id, nint iid, nint locale, nint flags, nint parameters, nint result, nint exception, nint argument);

    [PreserveSig] int GetCount(int* count);
    [PreserveSig] int Item(PropVariant index, nint* folder);
    [PreserveSig] int NewEnum(nint* enumerator);
    [PreserveSig] int Register(nint dispatch, int window, int windowClass, int* cookie);
    [PreserveSig] int RegisterPending(int thread, nint location, nint locationRoot, int windowClass, int* cookie);
    [PreserveSig] int Revoke(int cookie);
    [PreserveSig] int OnNavigate(int cookie, nint location);
    [PreserveSig] int OnActivated(int cookie, short active);
    [PreserveSig] int FindWindowSW(PropVariant* location, PropVariant* locationRoot, int windowClass, int* window, int options, nint* dispatch);
}

/// <summary>ServProv.h IServiceProvider.</summary>
[GeneratedComInterface]
[Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IServiceProvider
{
    [PreserveSig] int QueryService(Guid* service, Guid* interfaceId, nint* instance);
}

/// <summary>ShObjIdl_core.h IShellBrowser (IOleWindow's two methods first).</summary>
[GeneratedComInterface]
[Guid("000214E2-0000-0000-C000-000000000046")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IShellBrowser
{
    [PreserveSig] int GetWindow(nint* window);
    [PreserveSig] int ContextSensitiveHelp(int enterMode);
    [PreserveSig] int InsertMenusSB(nint menu, nint widths);
    [PreserveSig] int SetMenuSB(nint menu, nint shared, nint activeObject);
    [PreserveSig] int RemoveMenusSB(nint menu);
    [PreserveSig] int SetStatusTextSB(nint text);
    [PreserveSig] int EnableModelessSB(int enable);
    [PreserveSig] int TranslateAcceleratorSB(nint message, ushort id);
    [PreserveSig] int BrowseObject(nint pidl, uint flags);
    [PreserveSig] int GetViewStateStream(uint mode, nint stream);
    [PreserveSig] int GetControlWindow(uint id, nint* window);
    [PreserveSig] int SendControlMsg(uint id, uint message, nint wParam, nint lParam, nint result);
    [PreserveSig] int QueryActiveShellView(nint* view);
}

/// <summary>ShObjIdl_core.h IFolderView2 (IFolderView's 14 methods, then 15 of its own before GetSelection).</summary>
[GeneratedComInterface]
[Guid("1af3a467-214f-4298-908e-06b03e0b39f9")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IFolderView2
{
    // IFolderView
    [PreserveSig] int GetCurrentViewMode(nint mode);
    [PreserveSig] int SetCurrentViewMode(uint mode);
    [PreserveSig] int GetFolder(nint iid, nint folder);
    [PreserveSig] int Item(int index, nint pidl);
    [PreserveSig] int ItemCount(uint flags, nint count);
    [PreserveSig] int Items(uint flags, nint iid, nint items);
    [PreserveSig] int GetSelectionMarkedItem(nint item);
    [PreserveSig] int GetFocusedItem(nint item);
    [PreserveSig] int GetItemPosition(nint pidl, nint point);
    [PreserveSig] int GetSpacing(nint point);
    [PreserveSig] int GetDefaultSpacing(nint point);
    [PreserveSig] int GetAutoArrange();
    [PreserveSig] int SelectItem(int item, uint flags);
    [PreserveSig] int SelectAndPositionItems(uint count, nint pidls, nint points, uint flags);

    // IFolderView2
    [PreserveSig] int SetGroupBy(nint key, int ascending);
    [PreserveSig] int GetGroupBy(nint key, nint ascending);
    [PreserveSig] int SetViewProperty(nint pidl, nint key, nint value);
    [PreserveSig] int GetViewProperty(nint pidl, nint key, nint value);
    [PreserveSig] int SetTileViewProperties(nint pidl, nint list);
    [PreserveSig] int SetExtendedTileViewProperties(nint pidl, nint list);
    [PreserveSig] int SetText(int type, nint text);
    [PreserveSig] int SetCurrentFolderFlags(uint mask, uint flags);
    [PreserveSig] int GetCurrentFolderFlags(nint flags);
    [PreserveSig] int GetSortColumnCount(nint count);
    [PreserveSig] int SetSortColumns(nint columns, int count);
    [PreserveSig] int GetSortColumns(nint columns, int count);
    [PreserveSig] int GetItem(int item, nint iid, nint instance);
    [PreserveSig] int GetVisibleItem(int start, int previous, nint item);
    [PreserveSig] int GetSelectedItem(int start, nint item);
    [PreserveSig] int GetSelection(int noneImpliesFolder, nint* items);
}

/// <summary>ShObjIdl_core.h IShellItem.</summary>
[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IShellItem
{
    [PreserveSig] int BindToHandler(nint context, nint handler, nint iid, nint instance);
    [PreserveSig] int GetParent(nint* parent);
    [PreserveSig] int GetDisplayName(uint form, char** name);
}
