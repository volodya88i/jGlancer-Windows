using Avalonia.Threading;

namespace JGlancer.App.Viewer.Media;

/// <summary>
/// The tags Windows knows for songs (title, artist, album), read once per file off the UI thread and shared by the
/// player, the preview pane and Quick Preview, so a song shows its title and artist at once wherever it was seen before.
/// </summary>
internal static class SongTags
{
    private static readonly Dictionary<string, Platform.Windows.AudioTags.Tags?> Known = [];

    /// <summary>A song's tags were read (on the UI thread).</summary>
    public static event Action<string>? Read;

    /// <summary>The song's tags, or null until they are read (then <see cref="Read"/> is raised) or when it has none.</summary>
    public static Platform.Windows.AudioTags.Tags? Of(string path)
    {
        if (Known.TryGetValue(path, out var tags))
        {
            return tags;
        }
        Known[path] = null;
        _ = Task.Run(() => Platform.Windows.AudioTags.Read(path)).ContinueWith(read =>
        {
            Known[path] = read.Result;
            Dispatcher.UIThread.Post(() => Read?.Invoke(path));
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        return null;
    }
}
