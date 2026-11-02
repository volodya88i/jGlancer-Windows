using JGlancer.Core.Store;

namespace JGlancer.Core.FileOps;

/// <summary>File names as Windows allows them, and free names next to taken ones.</summary>
public static class FileNames
{
    private static readonly System.Buffers.SearchValues<char> Invalid = System.Buffers.SearchValues.Create("<>:\"/\\|?*");

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Whether Windows takes <paramref name="name"/> as a file name: none of <c>&lt; &gt; : " / \ | ? *</c> or control
    /// characters, no trailing dot or space, not a device name (CON, NUL, COM1, …).
    /// </summary>
    public static bool IsValid(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0 || name.Trim().Length == 0 || name.IndexOfAny(Invalid) >= 0 || name.Any(char.IsControl)
            || name.EndsWith('.') || name.EndsWith(' '))
        {
            return false;
        }
        var stem = name.Split('.')[0].TrimEnd();
        return !Reserved.Contains(stem);
    }

    /// <summary><paramref name="path"/>, or "name 2.ext", "name 3.ext", … when it is taken.</summary>
    public static string Vacant(string path, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(exists);
        if (!exists(path))
        {
            return path;
        }
        var folder = Path.GetDirectoryName(path) ?? "";
        var extension = Path.GetExtension(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        for (var number = 2; ; number++)
        {
            var candidate = Path.Combine(folder, $"{stem} {number}{extension}");
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Whether two paths name the same file (case-insensitively on Windows).</summary>
    public static bool Same(string a, string b) =>
        string.Equals(LibraryPaths.Normalize(a), LibraryPaths.Normalize(b), LibraryPaths.Comparison);
}
