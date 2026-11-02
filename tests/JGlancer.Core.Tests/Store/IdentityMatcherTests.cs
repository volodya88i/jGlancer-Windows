using JGlancer.Core.Store;

namespace JGlancer.Core.Tests.Store;

public sealed class IdentityMatcherTests
{
    private static IdentityCandidate Candidate(long id, ulong? fileId = null, long size = 10, long modified = 1, byte[]? hash = null, bool missing = true, bool sameVolume = true) =>
        new(id, fileId, size, modified, hash, missing, sameVolume, $@"C:\old\{id}");

    private static long? Match(IReadOnlyList<IdentityCandidate> candidates, long size = 10, long modified = 1, ulong? fileId = null,
        HashSet<long>? claimed = null, FakeProbe? probe = null, byte[]? hash = null) =>
        IdentityMatcher.Match(new IdentityMatcher.NewFile(size, modified, fileId), candidates, claimed ?? [], probe ?? new FakeProbe(), () => hash);

    [Fact]
    public void FileIdMatchNeedsSameVolumeAndSize()
    {
        Assert.Equal(1, Match([Candidate(1, fileId: 4)], fileId: 4));
        Assert.Null(Match([Candidate(1, fileId: 4, sameVolume: false)], fileId: 4));
        Assert.Null(Match([Candidate(1, fileId: 4)], size: 11, fileId: 4));
    }

    [Fact]
    public void PresentCandidateMatchesOnlyIfItsFileIsGone()
    {
        var present = Candidate(1, fileId: 4, missing: false);
        Assert.Equal(1, Match([present], fileId: 4));
        Assert.Null(Match([present], fileId: 4, probe: new FakeProbe { ExistingPaths = { @"C:\old\1" } }));
    }

    [Fact]
    public void ContentMatchNeedsSizeDateAndHash()
    {
        byte[] hash = [7];
        Assert.Equal(1, Match([Candidate(1, hash: hash)], hash: hash));
        Assert.Null(Match([Candidate(1, hash: hash)], modified: 2, hash: hash));
        Assert.Null(Match([Candidate(1, hash: hash)], hash: [8]));
        Assert.Null(Match([Candidate(1, hash: null)], hash: hash));
        Assert.Null(Match([Candidate(1, hash: hash, missing: false)], hash: hash));
    }

    [Fact]
    public void ClaimedAndAmbiguousCandidatesAreSkipped()
    {
        byte[] hash = [7];
        Assert.Null(Match([Candidate(1, hash: hash)], claimed: [1], hash: hash));
        Assert.Null(Match([Candidate(1, hash: hash), Candidate(2, hash: hash)], hash: hash));
    }
}
