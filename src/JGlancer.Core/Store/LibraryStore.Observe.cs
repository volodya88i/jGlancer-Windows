using System.Runtime.CompilerServices;
using System.Threading.Channels;
using JGlancer.Core.Model;
using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

public sealed partial class LibraryStore
{
    /// <summary>Emits the asset index for <paramref name="query"/> now and after every change that alters it.</summary>
    public IAsyncEnumerable<IReadOnlyList<AssetRef>> ObserveAssetRefs(AssetQuery query, CancellationToken cancellation = default) =>
        Observe(connection => FetchAssetRefs(connection, query), (a, b) => a.SequenceEqual(b), cancellation);

    /// <summary>
    /// Emits a root's present folders that hold media (in them or below; the root always) now and after every change.
    /// </summary>
    public IAsyncEnumerable<IReadOnlyList<Folder>> ObserveFolders(long rootId, CancellationToken cancellation = default) =>
        Observe(connection => FolderNode.WithMedia(
                FetchFolders(connection, rootId, includeMissing: false),
                Sql.Rows(connection, """
                    SELECT id FROM folder
                    WHERE root_id = @root AND state = 0 AND EXISTS (SELECT 1 FROM asset WHERE asset.folder_id = folder.id AND asset.state = 0)
                    """, r => r.GetInt64(0), ("@root", rootId)).ToHashSet()),
            (a, b) => a.SequenceEqual(b), cancellation);

    /// <summary>Emits the roots now and after every change.</summary>
    public IAsyncEnumerable<IReadOnlyList<RootFolder>> ObserveRoots(CancellationToken cancellation = default) =>
        Observe<IReadOnlyList<RootFolder>>(
            connection => Sql.Rows(connection, $"SELECT {RootRow.Columns} FROM root_folder ORDER BY id", r => RootRow.Read(r).ToModel()),
            (a, b) => a.SequenceEqual(b), cancellation);

    /// <summary>
    /// The value of <paramref name="fetch"/> now and after each committed write, skipping repeats. Writes that arrive
    /// while a fetch runs collapse into one more fetch (no polling).
    /// </summary>
    private async IAsyncEnumerable<T> Observe<T>(
        Func<SqliteConnection, T> fetch, Func<T, T, bool> equal, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        var signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        void OnChanged(object? sender, EventArgs e) => signals.Writer.TryWrite(true);
        Changed += OnChanged;
        try
        {
            var last = await ReadAsync(fetch).ConfigureAwait(false);
            yield return last;
            while (await signals.Reader.WaitToReadAsync(cancellation).ConfigureAwait(false))
            {
                signals.Reader.TryRead(out _);
                var next = await ReadAsync(fetch).ConfigureAwait(false);
                if (!equal(last, next))
                {
                    last = next;
                    yield return next;
                }
            }
        }
        finally
        {
            Changed -= OnChanged;
        }
    }
}
