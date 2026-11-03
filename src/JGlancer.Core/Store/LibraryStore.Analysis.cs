using JGlancer.Core.Audio;

namespace JGlancer.Core.Store;

/// <summary>
/// Tempo and key found in songs (W-107), by path, so any file opened counts, in the library or not. A row holds only
/// while the file keeps its size and modification time.
/// </summary>
public sealed partial class LibraryStore
{
    /// <summary>What was found in this version of the file, or null.</summary>
    public Task<AudioAnalysis?> AudioAnalysisAsync(string path, long size, long modifiedTicks)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ReadAsync<AudioAnalysis?>(connection => Sql.Rows(connection,
            "SELECT bpm, key_tonic, key_minor FROM audio_analysis WHERE path = @path AND size = @size AND mtime = @mtime",
            r => new AudioAnalysis(
                r.IsDBNull(0) ? null : r.GetDouble(0),
                r.IsDBNull(1) ? null : new MusicKey(r.GetInt32(1), r.GetInt32(2) != 0)),
            [("@path", path), ("@size", size), ("@mtime", modifiedTicks)]).FirstOrDefault());
    }

    /// <summary>Keeps <paramref name="analysis"/> for this version of the file, replacing any earlier one.</summary>
    public Task SaveAudioAnalysisAsync(string path, long size, long modifiedTicks, AudioAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(analysis);
        return WriteAsync(connection => Sql.Execute(connection,
            """
            INSERT OR REPLACE INTO audio_analysis (path, size, mtime, bpm, key_tonic, key_minor)
            VALUES (@path, @size, @mtime, @bpm, @tonic, @minor)
            """,
            ("@path", path), ("@size", size), ("@mtime", modifiedTicks),
            ("@bpm", analysis.Bpm is { } bpm ? bpm : DBNull.Value),
            ("@tonic", analysis.Key is { } key ? key.Tonic : DBNull.Value),
            ("@minor", analysis.Key is { IsMinor: true } ? 1 : 0)));
    }
}
