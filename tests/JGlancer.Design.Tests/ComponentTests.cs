using JGlancer.Tools;

namespace JGlancer.Design.Tests;

public sealed class ComponentTests
{
    [Fact]
    public void AccentTextMarksTheWordBetweenStars()
    {
        var parts = JGAccentText.Parse("Add a **folder**.");
        Assert.Equal([("Add a ", false), ("folder", true), (".", false)], parts);
    }

    [Theory]
    [InlineData("Plain text.")]
    [InlineData("Unclosed **marker.")]
    public void AccentTextWithoutAPairOfMarkersIsPlain(string markup) =>
        Assert.Equal([(markup, false)], JGAccentText.Parse(markup));

    [Fact]
    public void EveryListedIconWasGenerated()
    {
        var root = RepoPaths.FindRoot();
        var listed = File.ReadAllLines(RepoPaths.IconListInput(root))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(TokenGenerator.Pascal)
            .ToHashSet();
        var generated = typeof(JGIconData).GetFields().Select(f => f.Name).ToHashSet();
        Assert.True(listed.SetEquals(generated), "JGIconData.g.cs is stale: run the `icons` generator");
    }

    [Fact]
    public void JoinedPathsStartWithAnAbsoluteMove()
    {
        // lucide "x": two paths; the second starts with "m", absolute only while it is its own element.
        using var json = System.Text.Json.JsonDocument.Parse("""[["path",{"d":"M18 6 6 18"}],["path",{"d":"m6 6 12 12"}]]""");
        Assert.Equal("M18 6 6 18 M6 6l12 12", IconGenerator.PathData(json.RootElement));
    }

    [Theory]
    [InlineData("m9 18 6-6-6-6", "M9 18l6-6-6-6")]
    [InlineData("m14.622 17.897-10.68-2.913", "M14.622 17.897l-10.68-2.913")]
    [InlineData("m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21", "M21 15l-3.086-3.086a2 2 0 0 0-2.828 0L6 21")]
    [InlineData("m6 6h4", "M6 6h4")]
    [InlineData("M12 5v14", "M12 5v14")]
    public void ALeadingRelativeMoveBecomesAbsoluteAndItsLinesStayRelative(string path, string expected) =>
        Assert.Equal(expected, IconGenerator.Absolute(path));

    [Fact]
    public void EveryIconStaysOnItsCanvas()
    {
        foreach (var field in typeof(JGIconData).GetFields())
        {
            var data = (string)field.GetValue(null)!;
            var bounds = IconBounds.Of(data);
            Assert.True(bounds.Left >= -0.5 && bounds.Top >= -0.5 && bounds.Right <= 24.5 && bounds.Bottom <= 24.5, $"{field.Name}: {bounds} {data}");
        }
    }

    [Fact]
    public void RectanglesWithRoundCornersBecomeArcs()
    {
        using var json = System.Text.Json.JsonDocument.Parse("""[["rect",{"width":"18","height":"18","x":"3","y":"3","rx":"2"}]]""");
        Assert.Equal(
            "M5 3H19A2 2 0 0 1 21 5V19A2 2 0 0 1 19 21H5A2 2 0 0 1 3 19V5A2 2 0 0 1 5 3Z",
            IconGenerator.PathData(json.RootElement));
    }
}
