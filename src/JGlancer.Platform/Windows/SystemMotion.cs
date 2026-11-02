using System.Runtime.InteropServices;

namespace JGlancer.Platform.Windows;

/// <summary>Windows' "Animation effects" setting (Settings ▸ Accessibility ▸ Visual effects): the Reduce Motion counterpart.</summary>
public static partial class SystemMotion
{
    private const uint GetClientAreaAnimation = 0x1042;

    /// <summary>False when the user turned animation effects off; then fades are skipped (DESIGN.md §9).</summary>
    public static bool AnimationsEnabled
    {
        get
        {
            var enabled = 1;
            return !SystemParametersInfo(GetClientAreaAnimation, 0, ref enabled, 0) || enabled != 0;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint parameter, ref int value, uint update);
}
