using System.Globalization;
using JGlancer.Core.Model;

namespace JGlancer.Core.Tests.Model;

public sealed class PreviewTests
{
    private static Asset Video(double? duration, int? width, int? height, DateTimeOffset? captured = null) =>
        new(1, 1, "clip.mp4", @"C:\x\clip.mp4", MediaKind.Video, null, 12_400_000, new DateTimeOffset(2026, 3, 4, 5, 6, 0, TimeSpan.Zero),
            null, width, height, null, duration, null, null, captured, AssetState.Present, MetadataState.Extracted, AssetFlag.None);

    [Fact]
    public void TheFactsLineSkipsWhatIsUnknown()
    {
        var full = PreviewFacts.Line(Video(62, 1920, 1080));
        Assert.StartsWith("1:02 · 1920 × 1080 · ", full, StringComparison.Ordinal);
        Assert.DoesNotContain("×", PreviewFacts.Line(Video(null, null, null)), StringComparison.Ordinal);
        Assert.DoesNotContain("·", PreviewFacts.Line(Video(null, null, null)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDateIsTheCaptureDateWhenKnown()
    {
        var captured = new DateTimeOffset(2020, 1, 2, 12, 0, 0, TimeSpan.Zero);
        var shown = PreviewFacts.Date(Video(null, null, null, captured), CultureInfo.InvariantCulture);
        Assert.StartsWith(captured.ToLocalTime().ToString("d", CultureInfo.InvariantCulture), shown, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePreviewShowsTheFocusOrTheOnlySelectedFile()
    {
        var selection = new SelectionModel();
        Assert.Null(selection.Previewed);
        selection.Select(5);
        Assert.Equal(5, selection.Previewed);
        selection.Toggle(7);
        Assert.Equal(7, selection.Previewed);
        // Ctrl+click off the focus: the focus is no longer selected and two files are: nothing to preview.
        selection.Toggle(9);
        selection.Toggle(9);
        Assert.Equal(2, selection.Count);
        Assert.Equal(9, selection.Focus);
        Assert.Null(selection.Previewed);
        selection.Toggle(5);
        Assert.Equal(7, selection.Previewed);
    }
}
