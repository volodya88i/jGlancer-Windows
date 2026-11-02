namespace JGlancer.Tools;

/// <summary>Where generator inputs and outputs live in the repository.</summary>
internal static class RepoPaths
{
    /// <summary>The repository root: the nearest parent of the current directory (or of this assembly) with jGlancer.slnx.</summary>
    public static string FindRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "jGlancer.slnx")))
                {
                    return dir.FullName;
                }
            }
        }
        throw new InvalidOperationException("jGlancer.slnx not found above the current directory");
    }

    public static string TokensInput(string root) => Path.Combine(root, "docs", "design-tokens.json");

    public static string TokensOutput(string root) =>
        Path.Combine(root, "src", "JGlancer.Design", "Tokens", "JGTokens.g.cs");

    /// <summary>The macOS repository is expected next to this one (jGlancerMacOS).</summary>
    public static string DefaultXcstrings(string root) =>
        Path.Combine(root, "..", "jGlancerMacOS", "App", "Resources", "Localizable.xcstrings");

    public static string StringsOutput(string root, string language) =>
        Path.Combine(root, "src", "JGlancer.Core", "Localization", "Strings", $"mac.{language}.json");

    public static string IconListInput(string root) => Path.Combine(root, "tools", "JGlancer.Tools", "icons.txt");

    public static string IconsOutput(string root) =>
        Path.Combine(root, "src", "JGlancer.Design", "Icons", "JGIconData.g.cs");
}
