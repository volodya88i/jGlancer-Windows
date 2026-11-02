using System.Runtime.CompilerServices;
using JGlancer.Mpv;

namespace JGlancer.Platform.Tests;

/// <summary>Tests play fixtures without sound: no audio device is opened.</summary>
internal static class SilentPlayers
{
    [ModuleInitializer]
    internal static void Initialize() => MpvPlayer.IsSilent = true;
}
