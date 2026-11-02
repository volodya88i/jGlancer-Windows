using System.Globalization;

namespace JGlancer.Core.FileOps;

/// <summary>
/// Renaming many files at once (macOS BatchRenamePattern, FEATURES §7): a template of text and the tokens
/// <c>{name}</c> (the old name without its extension), <c>{n}</c> (a counter) and <c>{date}</c> (capture or
/// modification date, <c>yyyy-MM-dd</c>). Extensions are kept unless <see cref="NewExtension"/> names another one
/// (the file is not converted, macOS D-75).
/// </summary>
public sealed record BatchRenamePattern
{
    public const string NameToken = "{name}";
    public const string CounterToken = "{n}";
    public const string DateToken = "{date}";

    public string Template { get; init; } = NameToken;

    public int Start { get; init; } = 1;

    /// <summary>Counter digits with leading zeros; 0 uses as many as the last number needs.</summary>
    public int Digits { get; init; }

    /// <summary>The extension every file gets, with or without a leading dot; empty keeps each file's own.</summary>
    public string NewExtension { get; init; } = "";

    /// <summary>One file of the batch.</summary>
    public sealed record File(string Path, DateTimeOffset Date);

    /// <summary>Why a new name cannot be used.</summary>
    public enum Problem
    {
        Empty,

        /// <summary>A character or form Windows does not allow in names.</summary>
        InvalidCharacters,

        /// <summary>Two files of the batch would get the same name in one folder.</summary>
        Duplicate,

        /// <summary>Another file in the folder already has it.</summary>
        Taken,
    }

    /// <summary>The new names, in the order of <paramref name="files"/>.</summary>
    public IReadOnlyList<string> Names(IReadOnlyList<File> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var width = Digits > 0 ? Digits : (Start + Math.Max(files.Count - 1, 0)).ToString(CultureInfo.InvariantCulture).Length;
        return files.Select((file, index) =>
        {
            var extension = ReplacementExtension ?? System.IO.Path.GetExtension(file.Path).TrimStart('.');
            var counter = (Start + index).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
            var stem = Template
                .Replace(NameToken, System.IO.Path.GetFileNameWithoutExtension(file.Path), StringComparison.Ordinal)
                .Replace(CounterToken, counter, StringComparison.Ordinal)
                .Replace(DateToken, file.Date.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Trim();
            return stem.Length == 0 || extension.Length == 0 ? stem : stem + "." + extension;
        }).ToList();
    }

    /// <summary><see cref="NewExtension"/> without spaces and leading dots; null when empty (each file keeps its own).</summary>
    internal string? ReplacementExtension
    {
        get
        {
            var bare = NewExtension.Trim().TrimStart('.');
            return bare.Length == 0 ? null : bare;
        }
    }

    /// <summary>
    /// Problems per file (null when the name is fine). <paramref name="existing"/> lists the names already in a
    /// folder; names of the batch's own files do not count as taken. Names compare case-insensitively, as on NTFS.
    /// </summary>
    public static IReadOnlyList<Problem?> Problems(IReadOnlyList<File> files, IReadOnlyList<string> names, Func<string, IReadOnlySet<string>> existing)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(existing);
        static string Key(File file, string name) => (System.IO.Path.GetDirectoryName(file.Path) ?? "") + "\\" + name.ToUpperInvariant();
        var counts = files.Zip(names, Key).GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        var own = files.Select(file => Key(file, System.IO.Path.GetFileName(file.Path))).ToHashSet();
        var listed = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        return files.Zip(names, (file, name) =>
        {
            if (System.IO.Path.GetFileNameWithoutExtension(name).Trim().Length == 0)
            {
                return Problem.Empty;
            }
            if (!FileNames.IsValid(name))
            {
                return Problem.InvalidCharacters;
            }
            if (counts[Key(file, name)] > 1)
            {
                return Problem.Duplicate;
            }
            var folder = System.IO.Path.GetDirectoryName(file.Path) ?? "";
            if (!listed.TryGetValue(folder, out var inFolder))
            {
                inFolder = new HashSet<string>(existing(folder), StringComparer.OrdinalIgnoreCase);
                listed[folder] = inFolder;
            }
            return inFolder.Contains(name) && !own.Contains(Key(file, name)) ? Problem.Taken : (Problem?)null;
        }).ToList();
    }
}
