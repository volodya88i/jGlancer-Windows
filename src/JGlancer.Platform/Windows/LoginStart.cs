using System.Runtime.Versioning;
using Microsoft.Win32;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Starting the app's background helper at sign-in (Space in Explorer, W11): the current user's Run key, nothing for
/// other users and no administrator. Tests pass a key of their own.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LoginStart(RegistryKey run, string executable)
{
    private const string Name = "jGlancer";

    /// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
    public static LoginStart ForCurrentUser(string executable) =>
        new(Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"), executable);

    private string Command => $"\"{executable}\" --background";

    public bool IsEnabled => run.GetValue(Name) as string == Command;

    public void Enable() => run.SetValue(Name, Command);

    public void Disable() => run.DeleteValue(Name, throwOnMissingValue: false);
}
