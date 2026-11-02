namespace JGlancer.Core;

/// <summary>
/// The single code-level source of the app's identity (macOS D-01, D-68). Nothing else may spell the app's name or
/// identifier; the executable name lives in JGlancer.App.csproj (<c>AssemblyName</c>).
/// </summary>
public static class AppIdentity
{
    /// <summary>User-visible name.</summary>
    public const string Name = "jGlancer";

    /// <summary>Reverse-DNS identifier (macOS bundle id), used for ProgIDs and the single-instance pipe.</summary>
    public const string Identifier = "io.github.volodya88i.jglancer";

    /// <summary>Folder under %APPDATA% for library.sqlite, settings and the originals vault (user data, roams).</summary>
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);

    /// <summary>Folder under %LOCALAPPDATA% for cache.sqlite (rebuildable, machine-local).</summary>
    public static string CacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name, "Cache");
}
