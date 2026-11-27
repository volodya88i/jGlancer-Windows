using System.Runtime.InteropServices;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Windows' emoji panel (Win + .), opened for the text field that has the keyboard focus (the folder icon picker's
/// Emoji & Symbols, macOS D-129). Windows has no call for it in a desktop app, so the app presses the keys itself.
/// </summary>
public static partial class EmojiPanel
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort LeftWindows = 0x5B;
    private const ushort Period = 0xBE;

    /// <summary>Opens the panel; false when Windows refused the keys.</summary>
    public static bool Open()
    {
        Input[] keys =
        [
            Key(LeftWindows, 0), Key(Period, 0), Key(Period, KeyUp), Key(LeftWindows, KeyUp),
        ];
        return SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>()) == keys.Length;
    }

    private static Input Key(ushort key, uint flags) => new() { Type = InputKeyboard, VirtualKey = key, Flags = flags };

    /// <summary>INPUT with its KEYBDINPUT (the union is as large as MOUSEINPUT: 40 bytes in 64-bit processes).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public ushort VirtualKey;

        [FieldOffset(10)]
        public ushort ScanCode;

        [FieldOffset(12)]
        public uint Flags;

        [FieldOffset(16)]
        public uint Time;

        [FieldOffset(24)]
        public nint ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);
}
