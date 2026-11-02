using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows;

/// <summary>The color of a window's system title bar (windows without the app's own title bar).</summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class WindowCaption
{
    private const int CaptionColorAttribute = 35;

    /// <summary>Paints the title bar of <paramref name="window"/>; older than Windows 11 it stays as it is.</summary>
    public static void SetColor(nint window, uint argb)
    {
        // COLORREF: 0x00BBGGRR.
        var color = ((argb & 0xFF) << 16) | (argb & 0xFF00) | ((argb >> 16) & 0xFF);
        _ = DwmSetWindowAttribute(window, CaptionColorAttribute, &color, sizeof(uint));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, void* value, int size);
}
