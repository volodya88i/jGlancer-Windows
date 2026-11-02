using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Tests.Support;

/// <summary>
/// A unique folder under %TEMP% for one test, deleted afterwards. Tests only ever write here (CLAUDE.md: never the
/// real media).
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jGlancerTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Writes <paramref name="contents"/> at <paramref name="relativePath"/>, creating folders.</summary>
    public string Write(string relativePath, string contents = "x")
    {
        var target = Full(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        File.WriteAllText(target, contents);
        return target;
    }

    public void Move(string source, string destination)
    {
        var target = Full(destination);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        if (Directory.Exists(Full(source)))
        {
            Directory.Move(Full(source), target);
        }
        else
        {
            File.Move(Full(source), target);
        }
    }

    public void Remove(string relativePath)
    {
        var target = Full(relativePath);
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
        else
        {
            File.Delete(target);
        }
    }

    public string Full(string relativePath) => System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Dispose()
    {
        // Stores clear their own connection pools when disposed. ClearAllPools here would tear down the connections
        // of tests running in parallel.
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A watcher or scan still holding a handle; the folder is in %TEMP% and harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
