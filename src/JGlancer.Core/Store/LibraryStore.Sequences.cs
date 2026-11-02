using JGlancer.Core.Model;

namespace JGlancer.Core.Store;

public sealed partial class LibraryStore
{
    /// <summary>
    /// The image sequences among the photos <paramref name="query"/> shows (W-81): names and folders read here, the
    /// grouping is <see cref="ImageSequences.Find"/>. Empty for an album (its order is the user's) or without photos.
    /// </summary>
    public Task<IReadOnlyList<ImageSequence>> SequencesAsync(AssetQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Scope is QueryScope.AlbumScope || !query.Kinds.Contains(MediaKind.Photo))
        {
            return Task.FromResult<IReadOnlyList<ImageSequence>>([]);
        }
        return ReadAsync(connection =>
        {
            if (Filter(query with { Kinds = new HashSet<MediaKind> { MediaKind.Photo } }) is not { } filter)
            {
                return (IReadOnlyList<ImageSequence>)[];
            }
            var photos = Sql.Rows(connection, $"SELECT asset.id, asset.folder_id, asset.name FROM asset WHERE {filter.Sql}",
                r => new SequenceCandidate(r.GetInt64(0), r.GetInt64(1), r.GetString(2)), filter.Parameters);
            return ImageSequences.Find(photos);
        });
    }
}
