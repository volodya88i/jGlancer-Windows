namespace JGlancer.Core.Launch;

/// <summary>
/// What a launch asks for (W11): files and folders to open (Explorer's Open With, the context menu, a drop on the exe),
/// a file for Quick Preview (<c>--preview</c>, Explorer's "Quick Preview in jGlancer"), or the background helper
/// (<c>--background</c>, Space in Explorer). A second launch hands the same to the running app.
/// </summary>
public sealed record LaunchArguments(IReadOnlyList<string> Paths, string? Preview, bool Background)
{
    public static LaunchArguments None { get; } = new([], null, false);

    public bool IsEmpty => Paths.Count == 0 && Preview is null && !Background;

    /// <param name="valueOptions">Options followed by a value (<c>--data-dir X</c>): their values are not paths.</param>
    public static LaunchArguments Parse(IReadOnlyList<string> args, IReadOnlySet<string> valueOptions)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(valueOptions);
        var paths = new List<string>();
        string? preview = null;
        var background = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--preview", StringComparison.OrdinalIgnoreCase))
            {
                preview = i + 1 < args.Count ? Full(args[++i]) : null;
            }
            else if (string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase))
            {
                background = true;
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (valueOptions.Contains(arg))
                {
                    i++;
                }
            }
            else if (arg.Length > 0 && Full(arg) is { } path)
            {
                paths.Add(path);
            }
        }
        return new LaunchArguments(paths, preview, background);
    }

    /// <summary>One line per item, for the pipe to a running app (paths cannot contain line breaks on Windows).</summary>
    public string Serialize() => string.Join('\n', new[]
    {
        Background ? "background" : null,
        Preview is null ? null : "preview\t" + Preview,
    }.OfType<string>().Concat(Paths.Select(p => "open\t" + p)));

    public static LaunchArguments Deserialize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var paths = new List<string>();
        string? preview = null;
        var background = false;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 2);
            switch (parts[0])
            {
                case "background":
                    background = true;
                    break;
                case "preview" when parts.Length == 2:
                    preview = parts[1];
                    break;
                case "open" when parts.Length == 2:
                    paths.Add(parts[1]);
                    break;
            }
        }
        return new LaunchArguments(paths, preview, background);
    }

    private static string? Full(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim('"'));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
