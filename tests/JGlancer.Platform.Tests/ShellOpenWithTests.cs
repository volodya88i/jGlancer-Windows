using JGlancer.Platform.Windows;

namespace JGlancer.Platform.Tests;

/// <summary>Open With from the shell: read-only questions to Windows, nothing is opened.</summary>
public sealed class ShellOpenWithTests
{
    [Fact]
    public void RecommendedProgramsHaveNamesAndAtMostOneDefaultFirst()
    {
        var handlers = ShellOpenWith.Recommended(".jpg");
        try
        {
            Assert.All(handlers, h => Assert.False(string.IsNullOrWhiteSpace(h.DisplayName)));
            Assert.True(handlers.Count(h => h.IsDefault) <= 1);
            if (handlers.Any(h => h.IsDefault))
            {
                Assert.True(handlers[0].IsDefault);
            }
        }
        finally
        {
            foreach (var handler in handlers)
            {
                handler.Dispose();
            }
        }
    }

    [Fact]
    public void AnUnknownTypeHasNoRecommendedPrograms()
    {
        var handlers = ShellOpenWith.Recommended(".jglancer-test-nothing");
        Assert.Empty(handlers);
    }

    [Fact]
    public void ProgramNamesComeFromTheExecutable()
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        Assert.SkipUnless(File.Exists(notepad), "no notepad.exe");
        Assert.False(string.IsNullOrWhiteSpace(ShellOpenWith.ProgramName(notepad)));
        Assert.NotNull(ShellOpenWith.Icon(notepad, 16));
    }
}
