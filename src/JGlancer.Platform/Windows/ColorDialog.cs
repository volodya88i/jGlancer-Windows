using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows;

/// <summary>Windows' own color dialog (Settings → Appearance → Accent Color, W-93).</summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ColorDialog
{
    private const uint RgbInit = 0x1;
    private const uint FullOpen = 0x2;
    private const uint AnyColor = 0x100;
    private const int CustomColors = 16;
    private const uint Opaque = 0xFF000000;

    /// <summary>Blocks until the dialog closes; the chosen opaque ARGB color, or null when cancelled.</summary>
    public static uint? Choose(nint owner, uint argb)
    {
        var custom = stackalloc uint[CustomColors];
        new Span<uint>(custom, CustomColors).Clear();
        var request = new ChooseColorRequest
        {
            Size = (uint)sizeof(ChooseColorRequest),
            Owner = owner,
            Result = SwapRedBlue(argb),
            CustomColors = custom,
            Flags = RgbInit | FullOpen | AnyColor,
        };
        return ChooseColor(&request) ? Opaque | SwapRedBlue(request.Result) : null;
    }

    /// <summary>ARGB and COLORREF (0x00BBGGRR) keep red and blue the other way round.</summary>
    private static uint SwapRedBlue(uint color) => ((color & 0xFF) << 16) | (color & 0xFF00) | ((color >> 16) & 0xFF);

    [LibraryImport("comdlg32.dll", EntryPoint = "ChooseColorW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChooseColor(ChooseColorRequest* request);

    /// <summary>CHOOSECOLORW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ChooseColorRequest
    {
        public uint Size;
        public nint Owner;
        public nint Instance;
        public uint Result;
        public uint* CustomColors;
        public uint Flags;
        public nint CustomData;
        public nint Hook;
        public nint Template;
    }
}
