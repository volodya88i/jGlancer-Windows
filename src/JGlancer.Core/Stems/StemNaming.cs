using JGlancer.Core.FileOps;

namespace JGlancer.Core.Stems;

/// <summary>Where a split goes and what its files are called (macOS D-94, W-85).</summary>
public static class StemNaming
{
    /// <summary>
    /// A new folder next to the song: "&lt;song without extension&gt; split", or "… split 2", "… split 3" when taken.
    /// </summary>
    public static string DefaultFolder(string songPath, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(songPath);
        ArgumentNullException.ThrowIfNull(exists);
        var parent = Path.GetDirectoryName(songPath) ?? "";
        var name = Safe(Path.GetFileNameWithoutExtension(songPath)) + " split";
        var candidate = Path.Combine(parent, name);
        for (var number = 2; exists(candidate); number++)
        {
            candidate = Path.Combine(parent, $"{name} {number}");
        }
        return candidate;
    }

    /// <summary>"&lt;song&gt; - Vocals.wav".</summary>
    public static string FileName(string songPath, StemKind stem)
    {
        ArgumentNullException.ThrowIfNull(songPath);
        return Safe($"{Path.GetFileNameWithoutExtension(songPath)} - {stem}") + ".wav";
    }

    /// <summary>A name Windows takes: forbidden characters become "_", trailing dots and spaces go.</summary>
    public static string Safe(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var chars = name.Select(c => c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' || char.IsControl(c) ? '_' : c).ToArray();
        var safe = new string(chars).TrimEnd('.', ' ');
        if (safe.Length == 0)
        {
            safe = "_";
        }
        return FileNames.IsValid(safe) ? safe : "_" + safe;
    }
}
