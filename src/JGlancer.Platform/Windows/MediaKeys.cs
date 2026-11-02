using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows;

/// <summary>The keyboard's media keys.</summary>
public enum MediaKey
{
    PlayPause,
    Next,
    Previous,
}

/// <summary>
/// The keyboard's play/pause, next and previous keys (macOS D-92, W-84), wherever the focus is: registered as hot keys
/// on a thread of its own (its own message loop) only while <see cref="IsActive"/>, so other players get them back
/// whenever jGlancer plays nothing. A key another program already holds is simply not ours.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class MediaKeys : IDisposable
{
    private const uint HotKeyMessage = 0x0312; // WM_HOTKEY
    private const uint Quit = 0x0012; // WM_QUIT
    private const uint Register = 0x8001; // WM_APP + 1
    private const uint Unregister = 0x8002;
    private const uint NoRepeat = 0x4000; // MOD_NOREPEAT

    private static readonly (int Id, uint VirtualKey, MediaKey Key)[] Keys =
    [
        (1, 0xB3, MediaKey.PlayPause), // VK_MEDIA_PLAY_PAUSE
        (2, 0xB0, MediaKey.Next), // VK_MEDIA_NEXT_TRACK
        (3, 0xB1, MediaKey.Previous), // VK_MEDIA_PREV_TRACK
    ];

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private bool _active;

    public MediaKeys()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "jGlancer media keys" };
        _thread.Start();
        _started.Wait();
    }

    /// <summary>A media key was pressed. On the media keys' thread.</summary>
    public event Action<MediaKey>? Pressed;

    /// <summary>Whether the keys are taken: while one of the app's players has something to play.</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active != value && _threadId != 0)
            {
                _active = value;
                PostThreadMessage(_threadId, value ? Register : Unregister, 0, 0);
            }
        }
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        Message message;
        // Makes the thread's message queue before anyone posts to it.
        PeekMessage(&message, 0, 0, 0, 0);
        _started.Set();
        while (GetMessage(&message, 0, 0, 0) > 0)
        {
            switch (message.Id)
            {
                case Register:
                    foreach (var (id, key, _) in Keys)
                    {
                        RegisterHotKey(0, id, NoRepeat, key);
                    }
                    break;
                case Unregister:
                    foreach (var (id, _, _) in Keys)
                    {
                        UnregisterHotKey(0, id);
                    }
                    break;
                case HotKeyMessage:
                    foreach (var (id, _, key) in Keys)
                    {
                        if (id == (int)message.WParam)
                        {
                            Pressed?.Invoke(key);
                        }
                    }
                    break;
            }
        }
        foreach (var (id, _, _) in Keys)
        {
            UnregisterHotKey(0, id);
        }
    }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, Quit, 0, 0);
            _threadId = 0;
        }
        _started.Dispose();
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint window, int id);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(Message* message, nint window, uint min, uint max);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(Message* message, nint window, uint min, uint max, uint remove);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
