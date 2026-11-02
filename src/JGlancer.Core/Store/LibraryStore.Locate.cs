namespace JGlancer.Core.Store;

/// <summary>Where a file-system path sits in the library.</summary>
/// <param name="FolderId">The folder row of the path (a directory) or of its parent (a file); null if not indexed.</param>
/// <param name="AssetId">The present asset at the path, for files.</param>
public sealed record LibraryLocation(long RootId, long? FolderId, long? AssetId);

public sealed partial class LibraryStore
{
    /// <summary>Finds <paramref name="path"/> (canonical, absolute) under one of the roots; null when it is outside the library.</summary>
    public Task<LibraryLocation?> LocationAsync(string path, bool isDirectory) => ReadAsync(connection =>
    {
        var roots = Sql.Rows(connection, $"SELECT {RootRow.Columns} FROM root_folder", RootRow.Read);
        foreach (var root in roots)
        {
            if (LibraryPaths.Relative(path, root.Path) is not { } relative)
            {
                continue;
            }
            var folderPath = isDirectory ? relative : LibraryPaths.ParentOf(relative);
            var folderId = Sql.Long(connection, "SELECT id FROM folder WHERE root_id = @root AND rel_path = @path AND state = 0",
                ("@root", root.Id), ("@path", folderPath));
            long? assetId = null;
            if (!isDirectory && folderId is { } folder)
            {
                assetId = Sql.Long(connection, "SELECT id FROM asset WHERE folder_id = @folder AND name = @name AND state = 0",
                    ("@folder", folder), ("@name", LibraryPaths.NameOf(relative)));
            }
            return new LibraryLocation(root.Id, folderId, assetId);
        }
        return null;
    });
}
