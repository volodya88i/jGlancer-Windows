using JGlancer.Core.Model;
using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

/// <summary>A row of <c>asset</c>, mutable while a write transaction works on it.</summary>
internal sealed class AssetRow
{
    public const string Columns = """
        asset.id, asset.folder_id, asset.name, asset.name_key, asset.kind, asset.uti, asset.size, asset.mtime,
        asset.birth_time, asset.inode, asset.quick_hash, asset.width, asset.height, asset.orientation, asset.duration,
        asset.fps, asset.codec, asset.captured_at, asset.flag, asset.state, asset.missing_since, asset.meta_state,
        asset.indexed_at
        """;

    public long Id { get; set; }
    public long FolderId { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public int Kind { get; set; }
    public string? Uti { get; set; }
    public long Size { get; set; }
    public long Mtime { get; set; }
    public long? BirthTime { get; set; }
    public long? Inode { get; set; }
    public byte[]? QuickHash { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Orientation { get; set; }
    public double? Duration { get; set; }
    public double? Fps { get; set; }
    public string? Codec { get; set; }
    public long? CapturedAt { get; set; }
    public int Flag { get; set; }
    public int State { get; set; }
    public long? MissingSince { get; set; }
    public int MetaState { get; set; }
    public long IndexedAt { get; set; }

    public static AssetRow Read(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        FolderId = r.GetInt64(r.GetOrdinal("folder_id")),
        Name = r.GetString(r.GetOrdinal("name")),
        NameKey = r.GetString(r.GetOrdinal("name_key")),
        Kind = r.GetInt32(r.GetOrdinal("kind")),
        Uti = r.NullableString("uti"),
        Size = r.GetInt64(r.GetOrdinal("size")),
        Mtime = r.GetInt64(r.GetOrdinal("mtime")),
        BirthTime = r.NullableLong("birth_time"),
        Inode = r.NullableLong("inode"),
        QuickHash = r.NullableBlob("quick_hash"),
        Width = r.NullableInt("width"),
        Height = r.NullableInt("height"),
        Orientation = r.NullableInt("orientation"),
        Duration = r.NullableDouble("duration"),
        Fps = r.NullableDouble("fps"),
        Codec = r.NullableString("codec"),
        CapturedAt = r.NullableLong("captured_at"),
        Flag = r.GetInt32(r.GetOrdinal("flag")),
        State = r.GetInt32(r.GetOrdinal("state")),
        MissingSince = r.NullableLong("missing_since"),
        MetaState = r.GetInt32(r.GetOrdinal("meta_state")),
        IndexedAt = r.GetInt64(r.GetOrdinal("indexed_at")),
    };

    public Asset ToModel(string directory) => new(
        Id, FolderId, Name, LibraryPaths.Combine(directory, Name), (MediaKind)Kind, Uti, Size,
        UnixMilliseconds.ToDate(Mtime), BirthTime is { } birth ? UnixMilliseconds.ToDate(birth) : null,
        Width, Height, Orientation, Duration, Fps, Codec,
        CapturedAt is { } captured ? UnixMilliseconds.ToDate(captured) : null,
        (AssetState)State, (MetadataState)MetaState, (AssetFlag)Flag);

    /// <summary>Clears everything stage 2 derives from file content.</summary>
    public void ResetMetadata()
    {
        QuickHash = null;
        Width = null;
        Height = null;
        Orientation = null;
        Duration = null;
        Fps = null;
        Codec = null;
        CapturedAt = null;
        MetaState = (int)MetadataState.Pending;
    }

    public void Insert(SqliteConnection connection)
    {
        Id = Sql.Long(connection, """
            INSERT INTO asset (folder_id, name, name_key, kind, uti, size, mtime, birth_time, inode, quick_hash, width,
              height, orientation, duration, fps, codec, captured_at, flag, state, missing_since, meta_state, indexed_at)
            VALUES (@folder, @name, @key, @kind, @uti, @size, @mtime, @birth, @inode, @hash, @width, @height,
              @orientation, @duration, @fps, @codec, @captured, @flag, @state, @missing, @meta, @indexed)
            RETURNING id
            """, Parameters()) ?? 0;
    }

    public void Update(SqliteConnection connection)
    {
        Sql.Execute(connection, """
            UPDATE asset SET folder_id = @folder, name = @name, name_key = @key, kind = @kind, uti = @uti, size = @size,
              mtime = @mtime, birth_time = @birth, inode = @inode, quick_hash = @hash, width = @width, height = @height,
              orientation = @orientation, duration = @duration, fps = @fps, codec = @codec, captured_at = @captured,
              flag = @flag, state = @state, missing_since = @missing, meta_state = @meta, indexed_at = @indexed
            WHERE id = @id
            """, [.. Parameters(), ("@id", Id)]);
    }

    private (string, object?)[] Parameters() =>
    [
        ("@folder", FolderId), ("@name", Name), ("@key", NameKey), ("@kind", Kind), ("@uti", Uti), ("@size", Size),
        ("@mtime", Mtime), ("@birth", BirthTime), ("@inode", Inode), ("@hash", QuickHash), ("@width", Width),
        ("@height", Height), ("@orientation", Orientation), ("@duration", Duration), ("@fps", Fps), ("@codec", Codec),
        ("@captured", CapturedAt), ("@flag", Flag), ("@state", State), ("@missing", MissingSince), ("@meta", MetaState),
        ("@indexed", IndexedAt),
    ];
}

/// <summary>A row of <c>folder</c>.</summary>
internal sealed record FolderRow(long Id, long RootId, long? ParentId, string RelPath, string Name, int State)
{
    public const string Columns = "folder.id, folder.root_id, folder.parent_id, folder.rel_path, folder.name, folder.state";

    public static FolderRow Read(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetInt32(5));

    public Folder ToModel() => new(Id, RootId, ParentId, RelPath, Name, (AssetState)State);
}

/// <summary>A row of <c>root_folder</c>.</summary>
internal sealed record RootRow(long Id, string Path, string? VolumeId, bool IsRecursive, bool IsWatched, long AddedAt)
{
    public const string Columns = "id, path, volume_uuid, is_recursive, is_watched, added_at";

    public static RootRow Read(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt64(3) != 0, r.GetInt64(4) != 0, r.GetInt64(5));

    public RootFolder ToModel() => new(Id, Path, VolumeId, IsRecursive, IsWatched, UnixMilliseconds.ToDate(AddedAt));
}
