using JGlancer.Core.Scanning;
using JGlancer.Core.Store;
using JGlancer.Core.Tests.Support;
using JGlancer.Platform.Windows;

namespace JGlancer.Platform.Tests;

/// <summary>Access to the synthetic media in TestMedia/fixtures (generated on macOS, committed; never personal media).</summary>
internal static class Fixtures
{
    public static string Folder { get; } = Find();

    /// <summary>Copies fixtures into <paramref name="target"/> (tests never read or write the originals in place).</summary>
    public static void CopyTo(TemporaryDirectory target, params string[] names)
    {
        foreach (var name in names)
        {
            var destination = target.Full(name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(Folder, name), destination);
        }
    }

    public static LibraryScanner Scanner(LibraryStore store, TimeSpan? latency = null) =>
        new(store, new DirectoryLister(new WindowsDirectoryReader()), new WindowsMetadataReader(), new FolderWatcher(latency ?? TimeSpan.FromMilliseconds(100)));

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "TestMedia", "fixtures");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("TestMedia/fixtures not found");
    }
}
