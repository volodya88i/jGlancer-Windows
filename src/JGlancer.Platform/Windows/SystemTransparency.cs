using System.Runtime.Versioning;
using Microsoft.Win32;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Windows' "Transparency effects" (Settings ▸ Personalization ▸ Colors): without them the system's acrylic is drawn
/// as a plain color, so the see-through photo viewer (MAC-023) needs them on.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SystemTransparency
{
    /// <summary>True unless the user turned transparency effects off (an unreadable setting counts as on, Windows' default).</summary>
    public static bool Enabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("EnableTransparency") is not int value || value != 0;
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return true;
            }
        }
    }
}
