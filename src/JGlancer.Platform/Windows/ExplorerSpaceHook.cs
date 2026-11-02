using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Space in Explorer for Quick Preview (WINDOWS_PORT, the reason for a helper): a low-level keyboard hook on a
/// thread of its own (its own message loop, so a busy app never slows typing anywhere). The callback only looks at the
/// key and the focused window and returns at once: Space without modifiers while an Explorer window's file view (or
/// the desktop's) has the focus — never its address bar, search box or a name being edited — is held back and
/// reported; whoever handles it either previews the selection or gives the key back to Explorer (<see cref="Replay"/>).
/// While the preview is open, Space and Esc in Explorer close it and its arrows are reported after Explorer moved its
/// selection.
/// Every other key passes untouched.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class ExplorerSpaceHook : IDisposable
{
    /// <summary>Marks keys this hook sends itself (replayed Space), so it lets them through.</summary>
    private const nuint ReplayMarker = 0x6A476C6E; // "jGln"

    private static ExplorerSpaceHook? s_current;

    private readonly Thread _thread;
    private uint _threadId;
    private nint _hook;
    private volatile bool _previewOpen;
    private bool _spaceHeld;

    public ExplorerSpaceHook()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "jGlancer Explorer keys" };
    }

    /// <summary>Space in Explorer's file view: the Explorer window (top level) and whether it is the desktop. On the hook thread.</summary>
    public event Action<nint, bool>? SpacePressed;

    /// <summary>Space or Esc while the preview is open: it closes. On the hook thread.</summary>
    public event Action? CloseRequested;

    /// <summary>An arrow moved Explorer's selection while the preview is open. On the hook thread.</summary>
    public event Action<nint, bool>? SelectionMoved;

    /// <summary>The preview is on screen: Space and Esc are for it.</summary>
    public bool PreviewOpen
    {
        get => _previewOpen;
        set
        {
            _previewOpen = value;
            // Clicks in Explorer are watched only while the preview is open (nothing to do otherwise).
            if (_threadId != 0)
            {
                PostThreadMessage(_threadId, value && FollowsClicks ? WatchSelection : StopWatching, 0, 0);
            }
        }
    }

    /// <summary>Clicking other files in Explorer while the preview is open shows them (Settings → Windows).</summary>
    public bool FollowsClicks { get; set; } = true;

    private nint _selectionHook;

    public void Start()
    {
        if (s_current is not null)
        {
            throw new InvalidOperationException("One Explorer hook at a time.");
        }
        s_current = this;
        _thread.Start();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(KeyboardLowLevel, &Callback, GetModuleHandle(null), 0);
        Message message;
        while (GetMessage(&message, 0, 0, 0) > 0)
        {
            if (message.Window == 0 && message.Id == WatchSelection && _selectionHook == 0)
            {
                // Out of context, on this thread's message loop: Explorer's focus and selection changes.
                _selectionHook = SetWinEventHook(FocusEvent, SelectionWithinEvent, 0, &OnSelectionEvent, 0, 0, OutOfContext | SkipOwnProcess);
            }
            else if (message.Window == 0 && message.Id == StopWatching && _selectionHook != 0)
            {
                UnhookWinEvent(_selectionHook);
                _selectionHook = 0;
            }
        }
        if (_selectionHook != 0)
        {
            UnhookWinEvent(_selectionHook);
            _selectionHook = 0;
        }
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint Callback(int code, nint message, nint data)
    {
        var hook = s_current;
        if (code >= 0 && hook is not null && hook.Handle((int)message, (KeyInfo*)data))
        {
            // Held back: Explorer never sees this key.
            return 1;
        }
        return CallNextHookEx(0, code, message, data);
    }

    /// <summary>Focus or selection changed somewhere: if it is Explorer's file view in front, its selection moved.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnSelectionEvent(nint hook, uint eventId, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (s_current is { _previewOpen: true } current && FileViewInFront() is { } explorer)
        {
            current.SelectionMoved?.Invoke(explorer.Window, explorer.IsDesktop);
        }
    }

    /// <summary>True to hold the key back.</summary>
    private bool Handle(int message, KeyInfo* key)
    {
        if ((key->Flags & Injected) != 0 && key->ExtraInfo == ReplayMarker)
        {
            return false;
        }
        var down = message is KeyDown or SystemKeyDown;
        if (key->VirtualKey == SpaceKey && !down)
        {
            var held = _spaceHeld;
            _spaceHeld = false;
            return held;
        }
        if (!down || ModifierDown())
        {
            return false;
        }
        switch (key->VirtualKey)
        {
            case SpaceKey:
                if (_spaceHeld)
                {
                    // Auto-repeat of a Space already taken.
                    return true;
                }
                if (FileViewInFront() is not { } explorer)
                {
                    // Typing elsewhere; the preview window takes its own keys.
                    return false;
                }
                _spaceHeld = true;
                if (_previewOpen)
                {
                    CloseRequested?.Invoke();
                }
                else
                {
                    SpacePressed?.Invoke(explorer.Window, explorer.IsDesktop);
                }
                return true;
            case EscapeKey when _previewOpen && FileViewInFront() is not null:
                CloseRequested?.Invoke();
                return true;
            case >= LeftKey and <= DownKey when _previewOpen && FileViewInFront() is { } moved:
                // Explorer moves its selection first; the preview follows once it has.
                SelectionMoved?.Invoke(moved.Window, moved.IsDesktop);
                return false;
            default:
                return false;
        }
    }

    /// <summary>Gives a held-back Space to Explorer after all (the selection was not a media file).</summary>
    public static void Replay()
    {
        var inputs = stackalloc Input[2];
        inputs[0] = new Input { Type = KeyboardInput, Key = new KeyboardData { VirtualKey = SpaceKey, ExtraInfo = ReplayMarker } };
        inputs[1] = new Input { Type = KeyboardInput, Key = new KeyboardData { VirtualKey = SpaceKey, Flags = KeyUpFlag, ExtraInfo = ReplayMarker } };
        _ = SendInput(2, inputs, sizeof(Input));
    }

    /// <summary>Explorer's file view or the desktop's has the keyboard: its top-level window.</summary>
    private static (nint Window, bool IsDesktop)? FileViewInFront()
    {
        var window = GetForegroundWindow();
        if (window == 0)
        {
            return null;
        }
        var windowClass = ClassName(window);
        var isDesktop = windowClass is "Progman" or "WorkerW";
        if (!isDesktop && windowClass is not ("CabinetWClass" or "ExploreWClass"))
        {
            return null;
        }
        var info = new GuiThreadInfo { Size = sizeof(GuiThreadInfo) };
        if (!GetGUIThreadInfo(GetWindowThreadProcessId(window, null), &info) || info.Focus == 0)
        {
            return null;
        }
        // The item list itself ("DirectUIHWND", the desktop's "SysListView32") inside the folder view, not a name
        // being edited ("Edit") nor the address bar or search box (outside the folder view).
        var focusClass = ClassName(info.Focus);
        if (focusClass is not ("DirectUIHWND" or "SysListView32") || ClassName(GetParent(info.Focus)) != "SHELLDLL_DefView")
        {
            return null;
        }
        return (window, isDesktop);
    }

    /// <summary>The high bit of GetAsyncKeyState: the key is down now.</summary>
    private static bool ModifierDown() =>
        GetAsyncKeyState(ControlKey) < 0 || GetAsyncKeyState(AltKey) < 0 || GetAsyncKeyState(ShiftKey) < 0
            || GetAsyncKeyState(LeftWindowsKey) < 0 || GetAsyncKeyState(RightWindowsKey) < 0;

    private static string ClassName(nint window)
    {
        var buffer = stackalloc char[64];
        var length = GetClassName(window, buffer, 64);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, Quit, 0, 0);
        }
        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }
    }

    private const int KeyboardLowLevel = 13;
    private const uint WatchSelection = 0x8001; // WM_APP + 1
    private const uint StopWatching = 0x8002;
    private const uint FocusEvent = 0x8005;
    private const uint SelectionWithinEvent = 0x8009;
    private const uint OutOfContext = 0x0000;
    private const uint SkipOwnProcess = 0x0002;
    private const int KeyDown = 0x0100;
    private const int SystemKeyDown = 0x0104;
    private const uint Quit = 0x0012;
    private const uint Injected = 0x10;
    private const ushort SpaceKey = 0x20;
    private const ushort EscapeKey = 0x1B;
    private const ushort LeftKey = 0x25;
    private const ushort DownKey = 0x28;
    private const int ShiftKey = 0x10;
    private const int ControlKey = 0x11;
    private const int AltKey = 0x12;
    private const int LeftWindowsKey = 0x5B;
    private const int RightWindowsKey = 0x5C;
    private const uint KeyboardInput = 1;
    private const uint KeyUpFlag = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInfo
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public int CaretLeft;
        public int CaretTop;
        public int CaretRight;
        public int CaretBottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    /// <summary>INPUT with the keyboard member; padded to the size of the largest member (MOUSEINPUT).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public KeyboardData Key;
    }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    private static partial nint SetWindowsHookEx(int hookType, delegate* unmanaged[Stdcall]<int, nint, nint, nint> callback, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint SetWinEventHook(uint min, uint max, nint module,
        delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void> callback, uint process, uint thread, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(Message* message, nint window, uint min, uint max);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? name);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial nint GetParent(nint window);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, uint* process);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint thread, GuiThreadInfo* info);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static partial int GetClassName(nint window, char* name, int size);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, Input* inputs, int size);
}
