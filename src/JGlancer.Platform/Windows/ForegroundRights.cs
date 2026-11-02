using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows;

/// <summary>Windows' foreground rule: a launch that may come to the front can pass that right on to the running app.</summary>
[SupportedOSPlatform("windows")]
public static partial class ForegroundRights
{
    private const int AnyProcess = -1;

    /// <summary>Lets any process (the running app) bring its window to the front next.</summary>
    public static void PassOn() => _ = AllowSetForegroundWindow(AnyProcess);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
