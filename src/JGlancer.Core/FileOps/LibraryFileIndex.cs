using JGlancer.Core.Store;

namespace JGlancer.Core.FileOps;

/// <summary>
/// Keeps the library in step with the app's file operations (macOS LibraryFileIndex): moved files keep their asset
/// id, files sent to the Recycle Bin become tombstones and come back with the same id on undo. Copies and new folders
/// are left to the scanner.
/// </summary>
public sealed class LibraryFileIndex(LibraryStore store) : IFileIndex
{
    public Task MovedAsync(string source, string destination) => store.MoveAssetAsync(source, destination);

    public Task RemovedAsync(string path) => store.MarkMissingAsync(path);

    public Task RestoredAsync(string path) => store.ReviveAsync(path);
}
