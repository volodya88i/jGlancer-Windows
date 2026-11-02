namespace JGlancer.Core.Store;

/// <summary>An existing asset that a newly found file might be (macOS ARCHITECTURE §13).</summary>
/// <param name="IsSameVolume">Whether the candidate's root is on the same volume as the new file (file ids are per volume).</param>
internal sealed record IdentityCandidate(
    long Id,
    ulong? FileId,
    long Size,
    long ModifiedAt,
    byte[]? QuickHash,
    bool IsMissing,
    bool IsSameVolume,
    string Path);

/// <summary>
/// Decides whether a new file is an existing asset that was moved or renamed outside the app (macOS D-31). Best
/// effort:
/// 1. Same volume and file id, same size, and the candidate's old path no longer exists (or is a tombstone).
/// 2. Otherwise a tombstone with equal size, modification date and quick hash, if exactly one matches (copies across
///    volumes change the file id).
/// Anything else is a new asset; the old row stays a tombstone.
/// </summary>
internal static class IdentityMatcher
{
    /// <summary>Stage-1 facts of the newly found file.</summary>
    public readonly record struct NewFile(long Size, long ModifiedAt, ulong? FileId);

    public static long? Match(
        NewFile file,
        IReadOnlyList<IdentityCandidate> candidates,
        IReadOnlySet<long> claimed,
        IFileProbe probe,
        Func<byte[]?> newFileHash)
    {
        var open = candidates.Where(c => !claimed.Contains(c.Id)).ToList();
        if (file.FileId is { } fileId)
        {
            var hit = open.FirstOrDefault(c => c.IsSameVolume && c.FileId == fileId && c.Size == file.Size
                && (c.IsMissing || !probe.FileExists(c.Path)));
            if (hit is not null)
            {
                return hit.Id;
            }
        }
        var byContent = open.Where(c => c.IsMissing && c.Size == file.Size && c.ModifiedAt == file.ModifiedAt && c.QuickHash is not null).ToList();
        if (byContent.Count == 0 || newFileHash() is not { } hash)
        {
            return null;
        }
        var hits = byContent.Where(c => c.QuickHash!.AsSpan().SequenceEqual(hash)).ToList();
        return hits.Count == 1 ? hits[0].Id : null;
    }
}
