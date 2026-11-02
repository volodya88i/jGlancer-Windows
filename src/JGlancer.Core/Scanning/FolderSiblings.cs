using JGlancer.Core.Model;

namespace JGlancer.Core.Scanning;

/// <summary>
/// The files a viewer pages through for a file opened from outside the library (macOS D-63, D-65): the media files of
/// its folder of the same kind, shallow, in natural name order; hidden and system files left out, as Explorer shows.
/// </summary>
public static class FolderSiblings
{
    /// <summary>The paths of <paramref name="file"/>'s folder of <paramref name="kind"/> (null: every kind), the file itself included.</summary>
    public static IReadOnlyList<string> Of(string file, MediaKind? kind)
    {
        ArgumentNullException.ThrowIfNull(file);
        var folder = Path.GetDirectoryName(file);
        IEnumerable<string> names = [];
        if (folder is not null)
        {
            try
            {
                names = Directory.EnumerateFiles(folder, "*", new EnumerationOptions
                {
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                    IgnoreInaccessible = true,
                }).Select(path => Path.GetFileName(path)).ToList();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
        var siblings = Choose(names, kind).Select(name => Path.Combine(folder ?? "", name)).ToList();
        if (!siblings.Contains(file, StringComparer.OrdinalIgnoreCase))
        {
            // A hidden file opened on purpose still shows (alone, or where it sorts).
            siblings.Add(file);
            siblings.Sort((a, b) => NaturalSortKey.Compare(Path.GetFileName(a), Path.GetFileName(b)));
        }
        return siblings;
    }

    /// <summary>The names of <paramref name="kind"/> (null: every media kind) in natural order (dot files left out).</summary>
    public static IReadOnlyList<string> Choose(IEnumerable<string> names, MediaKind? kind)
    {
        ArgumentNullException.ThrowIfNull(names);
        return [.. names
            .Where(name => !name.StartsWith('.') && MediaClassifier.Classify(name) is { } type && (kind is null || type.Kind == kind))
            .OrderBy(NaturalSortKey.Make, StringComparer.Ordinal)];
    }
}
