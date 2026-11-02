using JGlancer.Core.Model;

namespace JGlancer.Core.Tests.Model;

public sealed class ImageSequenceTests
{
    private static IEnumerable<SequenceCandidate> Frames(string pattern, int from, int to, long folder = 1, long firstId = 1) =>
        Enumerable.Range(from, to - from + 1).Select(n => new SequenceCandidate(firstId + n - from, folder, string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, n)));

    [Fact]
    public void ConsecutiveFramesOfOneFolderAreOneSequenceInFrameOrder()
    {
        var shuffled = Frames("shot_{0:000}.png", 1, 90).Reverse().ToList();
        var sequence = Assert.Single(ImageSequences.Find(shuffled));
        Assert.Equal(90, sequence.Frames.Count);
        Assert.Equal(1, sequence.Head);
        Assert.Equal(90, sequence.Frames[^1]);
    }

    [Fact]
    public void ShortRunsCameraNamesOtherFoldersAndWidthsAreNotSequences()
    {
        Assert.Empty(ImageSequences.Find(Frames("shot_{0:000}.png", 1, 4)));
        Assert.Empty(ImageSequences.Find(Frames("IMG_{0:0000}.JPG", 1000, 1020)));
        Assert.Empty(ImageSequences.Find(Frames("DSC_{0:0000}.jpg", 1, 30)));
        // Four frames in each of two folders, or two different extensions: no run of five.
        Assert.Empty(ImageSequences.Find(Frames("a{0:00}.png", 1, 4, folder: 1).Concat(Frames("a{0:00}.png", 5, 8, folder: 2, firstId: 100))));
        Assert.Empty(ImageSequences.Find(Frames("a{0:00}.png", 1, 3).Concat(Frames("a{0:00}.jpg", 4, 6, firstId: 100))));
    }

    [Fact]
    public void AGapSplitsTheFramesIntoTwoSequences()
    {
        var frames = Frames("render.{0:0000}.exr", 1, 10).Concat(Frames("render.{0:0000}.exr", 20, 26, firstId: 100));
        var sequences = ImageSequences.Find(frames).OrderBy(s => s.Head).ToList();
        Assert.Equal([10, 7], sequences.Select(s => s.Frames.Count));
        // Case and the text before the number do not matter beyond the name itself.
        Assert.Single(ImageSequences.Find(Frames("Frame{0}.PNG", 1, 12)));
    }
}
