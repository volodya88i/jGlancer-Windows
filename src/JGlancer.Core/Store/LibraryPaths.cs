namespace JGlancer.Core.Store;

/// <summary>
/// Path rules of the library. Folder paths inside a root are stored <c>/</c>-separated (as on macOS, so the
/// subtree range query <c>rel_path &gt;= 'a/' AND rel_path &lt; 'a0'</c> works); absolute paths use the platform
/// separator. Windows paths compare case-insensitively.
/// </summary>
public static class LibraryPaths
{
    /// <summary>How absolute paths compare on this platform.</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>A path without a trailing separator, except for a volume root (<c>C:\</c>, <c>/</c>).</summary>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
        {
            return path.Length > 0 ? path[..1] : path;
        }
        // "C:" alone means the current directory of drive C; keep the separator of a drive root.
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + Path.DirectorySeparatorChar : trimmed;
    }

    /// <summary><paramref name="name"/> inside <paramref name="directory"/>.</summary>
    public static string Combine(string directory, string name)
    {
        ArgumentNullException.ThrowIfNull(directory);
        return directory.EndsWith(Path.DirectorySeparatorChar) ? directory + name : directory + Path.DirectorySeparatorChar + name;
    }

    /// <summary>The absolute path of a folder inside a root.</summary>
    public static string Absolute(string rootPath, string relativePath) =>
        relativePath.Length == 0 ? rootPath : Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Whether <paramref name="path"/> is <paramref name="ancestor"/> or lies below it.</summary>
    public static bool IsSameOrInside(string path, string ancestor)
    {
        if (string.Equals(path, ancestor, Comparison))
        {
            return true;
        }
        var prefix = ancestor.EndsWith(Path.DirectorySeparatorChar) ? ancestor : ancestor + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, Comparison);
    }

    /// <summary>The <c>/</c>-separated path of <paramref name="path"/> relative to <paramref name="rootPath"/>, or null outside it.</summary>
    public static string? Relative(string path, string rootPath)
    {
        var normalized = Normalize(path);
        if (string.Equals(normalized, rootPath, Comparison))
        {
            return "";
        }
        if (!IsSameOrInside(normalized, rootPath))
        {
            return null;
        }
        var start = rootPath.EndsWith(Path.DirectorySeparatorChar) ? rootPath.Length : rootPath.Length + 1;
        return normalized[start..].Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>The parent of a <c>/</c>-separated relative path ("" for a top-level folder).</summary>
    public static string ParentOf(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash < 0 ? "" : relativePath[..slash];
    }

    /// <summary>The last component of a <c>/</c>-separated relative path.</summary>
    public static string NameOf(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash < 0 ? relativePath : relativePath[(slash + 1)..];
    }

    /// <summary>The display name of a root folder ("Pictures", or "D:" for a drive root).</summary>
    public static string RootName(string rootPath)
    {
        var name = Path.GetFileName(rootPath);
        return name.Length > 0 ? name : rootPath.TrimEnd(Path.DirectorySeparatorChar);
    }
}
