using JGlancer.Core.Model;

namespace JGlancer.Core.Store;

/// <summary>An asset waiting for stage-2 metadata extraction.</summary>
/// <param name="ModifiedAt">Modification date seen at stage 1; results are saved only if the file did not change since.</param>
public sealed record PendingMetadataItem(long Id, string Path, MediaKind Kind, DateTimeOffset ModifiedAt);

/// <summary>Stage-2 results for one asset.</summary>
public sealed record MetadataUpdate(long Id, DateTimeOffset ModifiedAt, MetadataState State)
{
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Orientation { get; init; }
    public double? Duration { get; init; }
    public double? FrameRate { get; init; }
    public string? Codec { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public byte[]? QuickHash { get; init; }
}

public sealed partial class LibraryStore
{
    /// <summary>Up to <paramref name="limit"/> present assets still waiting for stage 2, with ids above <paramref name="afterId"/>, in id order.</summary>
    public Task<IReadOnlyList<PendingMetadataItem>> PendingMetadataAsync(long afterId = 0, int limit = 128) =>
        ReadAsync<IReadOnlyList<PendingMetadataItem>>(connection => Sql.Rows(connection, """
            SELECT asset.id, asset.kind, asset.mtime, asset.name, folder.rel_path, root_folder.path
            FROM asset JOIN folder ON folder.id = asset.folder_id JOIN root_folder ON root_folder.id = folder.root_id
            WHERE asset.meta_state = 0 AND asset.state = 0 AND asset.id > @after
            ORDER BY asset.id LIMIT @limit
            """, r => new PendingMetadataItem(
                r.GetInt64(0),
                LibraryPaths.Combine(LibraryPaths.Absolute(r.GetString(5), r.GetString(4)), r.GetString(3)),
                (MediaKind)r.GetInt32(1),
                UnixMilliseconds.ToDate(r.GetInt64(2))),
            ("@after", afterId), ("@limit", limit)));

    /// <summary>Saves a batch of stage-2 results in one transaction. Rows whose file changed meanwhile are skipped.</summary>
    public Task SaveMetadataAsync(IReadOnlyList<MetadataUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            return Task.CompletedTask;
        }
        return WriteAsync(connection =>
        {
            using var command = Sql.Command(connection, """
                UPDATE asset SET meta_state = @state, width = @width, height = @height, orientation = @orientation,
                  duration = @duration, fps = @fps, codec = @codec, captured_at = @captured, quick_hash = @hash
                WHERE id = @id AND mtime = @mtime
                """,
                ("@state", 0), ("@width", null), ("@height", null), ("@orientation", null), ("@duration", null), ("@fps", null),
                ("@codec", null), ("@captured", null), ("@hash", null), ("@id", 0L), ("@mtime", 0L));
            command.Prepare();
            foreach (var update in updates)
            {
                Set("@state", (int)update.State);
                Set("@width", update.Width);
                Set("@height", update.Height);
                Set("@orientation", update.Orientation);
                Set("@duration", update.Duration);
                Set("@fps", update.FrameRate);
                Set("@codec", update.Codec);
                Set("@captured", update.CapturedAt is { } captured ? UnixMilliseconds.From(captured) : null);
                Set("@hash", update.QuickHash);
                Set("@id", update.Id);
                Set("@mtime", UnixMilliseconds.From(update.ModifiedAt));
                command.ExecuteNonQuery();
            }
            void Set(string name, object? value) => command.Parameters[name].Value = value ?? DBNull.Value;
        });
    }
}
