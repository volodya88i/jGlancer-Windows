using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows;

/// <summary>
/// A black child window inside the app's window for mpv's own Direct3D 11 output (HDR, W-87): mpv draws into it with
/// a swap chain of its own, which can be HDR10 while the app's window is not. It is disabled, so the pointer goes
/// through to the app's window under it (clicks, the wheel, drags keep working); it takes no keyboard focus.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class NativeVideoHost : IDisposable
{
    private const string ClassName = "jGlancerVideoHost";
    private const uint ChildStyle = 0x40000000 | 0x10000000 | 0x08000000 | 0x04000000 | 0x02000000; // CHILD | VISIBLE | DISABLED | CLIPSIBLINGS | CLIPCHILDREN
    private static bool s_registered;

    private NativeVideoHost(nint handle)
    {
        Handle = handle;
    }

    /// <summary>The window mpv draws into (its <c>wid</c>).</summary>
    public nint Handle { get; private set; }

    /// <summary>Makes the window inside <paramref name="parent"/> (the app's top-level window), at no size yet.</summary>
    public static NativeVideoHost Create(nint parent)
    {
        Register();
        var handle = CreateWindowEx(0, ClassName, "", ChildStyle, 0, 0, 0, 0, parent, 0, GetModuleHandle(null), 0);
        if (handle == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
        }
        return new NativeVideoHost(handle);
    }

    /// <summary>Places the window (physical pixels of the parent's client area).</summary>
    public void Place(int x, int y, int width, int height, bool visible)
    {
        if (Handle == 0)
        {
            return;
        }
        // SWP_NOZORDER | SWP_NOACTIVATE, and SHOW / HIDE.
        SetWindowPos(Handle, 0, x, y, Math.Max(0, width), Math.Max(0, height), 0x0004 | 0x0010 | (visible ? 0x0040u : 0x0080u));
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            DestroyWindow(Handle);
            Handle = 0;
        }
    }

    private static unsafe void Register()
    {
        if (s_registered)
        {
            return;
        }
        var user32 = NativeLibrary.Load("user32.dll");
        fixed (char* name = ClassName)
        {
            var info = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                // No managed window procedure: Windows' default handles everything.
                Procedure = NativeLibrary.GetExport(user32, "DefWindowProcW"),
                Instance = GetModuleHandle(null),
                Background = GetStockObject(4), // BLACK_BRUSH
                ClassName = name,
            };
            RegisterClassEx(&info);
        }
        s_registered = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")]
    private static unsafe partial ushort RegisterClassEx(WindowClass* info);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? name);

    [LibraryImport("gdi32.dll")]
    private static partial nint GetStockObject(int index);
}
