using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace JGlancer.Core.Scanning;

/// <summary>
/// Watches a directory tree (ReadDirectoryChangesW through <see cref="FileSystemWatcher"/>) and delivers batches of
/// events collected for <see cref="Latency"/> after the first one. No polling: the system wakes us only when something
/// changed. Unlike FSEvents there is no history, so changes made while the app was closed are found by the diff-scan
/// at start (W-16).
/// </summary>
public sealed class FolderWatcher(TimeSpan? latency = null)
{
    /// <summary>Events closer together than this are delivered as one batch.</summary>
    public TimeSpan Latency { get; } = latency ?? TimeSpan.FromMilliseconds(500);

    /// <summary>Batches of events below <paramref name="path"/> until <paramref name="cancellation"/> fires.</summary>
    public async IAsyncEnumerable<IReadOnlyList<FileSystemEvent>> Events(string path, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        var channel = Channel.CreateUnbounded<FileSystemEvent>(new UnboundedChannelOptions { SingleReader = true });
        using var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };
        void Post(FileSystemEvent e) => channel.Writer.TryWrite(e);
        watcher.Created += (_, e) => Post(new FileSystemEvent(FileSystemChange.Created, e.FullPath, IsDirectory: Directory.Exists(e.FullPath)));
        watcher.Changed += (_, e) => Post(new FileSystemEvent(FileSystemChange.Changed, e.FullPath, IsDirectory: Directory.Exists(e.FullPath)));
        watcher.Deleted += (_, e) => Post(new FileSystemEvent(FileSystemChange.Deleted, e.FullPath));
        watcher.Renamed += (_, e) => Post(new FileSystemEvent(FileSystemChange.Renamed, e.FullPath, e.OldFullPath, Directory.Exists(e.FullPath)));
        watcher.Error += (_, _) => Post(new FileSystemEvent(FileSystemChange.Overflow, path));
        watcher.EnableRaisingEvents = true;

        var reader = channel.Reader;
        while (await reader.WaitToReadAsync(cancellation).ConfigureAwait(false))
        {
            await Task.Delay(Latency, cancellation).ConfigureAwait(false);
            var batch = new List<FileSystemEvent>();
            while (reader.TryRead(out var next))
            {
                batch.Add(next);
            }
            yield return batch;
        }
    }
}
