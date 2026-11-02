namespace JGlancer.Core.FileOps;

/// <summary>A file operation the user asked for (FEATURES §7).</summary>
public abstract record FileOperation
{
    public sealed record Move(IReadOnlyList<string> Sources, string Folder) : FileOperation;

    public sealed record Copy(IReadOnlyList<string> Sources, string Folder) : FileOperation;

    public sealed record Rename(string Source, string Name) : FileOperation;

    /// <summary>Many renames as one undoable batch (<see cref="BatchRenamePattern"/>); names may trade places.</summary>
    public sealed record RenameAll(IReadOnlyList<(string Source, string Name)> Items) : FileOperation;

    /// <summary>To the Recycle Bin.</summary>
    public sealed record Trash(IReadOnlyList<string> Sources) : FileOperation;

    public sealed record CreateFolder(string Parent, string Name) : FileOperation;

    public UndoKind Kind => this switch
    {
        Move => UndoKind.Move,
        Copy => UndoKind.Copy,
        Rename or RenameAll => UndoKind.Rename,
        Trash => UndoKind.Trash,
        _ => UndoKind.CreateFolder,
    };
}

/// <summary>
/// One reversible file change (macOS FileStep). Running a step returns the step that undoes it, so an operation's
/// undo record is the inverses of its steps in reverse order, and undoing an undo record yields the redo record.
/// </summary>
public abstract record FileStep
{
    /// <summary>Moves a file (another volume: copy, verify, then delete the source). Undo: move back.</summary>
    public sealed record Move(string From, string To) : FileStep;

    /// <summary>Copies a file. Undo: send the copy to the Recycle Bin.</summary>
    public sealed record Copy(string From, string To) : FileStep;

    /// <summary>Sends a file to the Recycle Bin. Undo: restore it.</summary>
    public sealed record Trash(string Path) : FileStep;

    /// <summary>Takes a file out of the Recycle Bin (<paramref name="Trashed"/>, where it lies there) back to <paramref name="Original"/>.</summary>
    public sealed record Restore(string Trashed, string Original) : FileStep;

    /// <summary>Creates an empty folder. Undo: send it to the Recycle Bin.</summary>
    public sealed record CreateFolder(string Path) : FileStep;

    /// <summary>The file a step is about (for failures).</summary>
    public string Subject => this switch
    {
        Move move => move.From,
        Copy copy => copy.From,
        Trash trash => trash.Path,
        Restore restore => restore.Original,
        CreateFolder folder => folder.Path,
        _ => "",
    };
}

public enum UndoKind
{
    Move,
    Copy,
    Rename,
    Trash,
    CreateFolder,

    /// <summary>The files and folder a stem split made (W-85): undoing moves them to the Recycle Bin.</summary>
    SplitStems,
}

/// <summary>What undoes (or redoes) one operation: steps to run in order.</summary>
public sealed record UndoRecord(UndoKind Kind, IReadOnlyList<FileStep> Steps)
{
    public bool IsEmpty => Steps.Count == 0;
}

/// <summary>How a batch went.</summary>
public sealed class FileOperationResult(UndoKind kind)
{
    /// <summary>Items done.</summary>
    public int Completed { get; set; }

    public List<string> Skipped { get; } = [];

    public List<string> Failures { get; } = [];

    public bool WasCancelled { get; set; }

    /// <summary>Where the done items are now (moved, copied, renamed, restored or created).</summary>
    public List<string> Results { get; } = [];

    /// <summary>Undoes what was done (empty when nothing was).</summary>
    public UndoRecord Undo { get; set; } = new(kind, []);
}

/// <summary>Progress of a batch: items finished of the total.</summary>
public readonly record struct FileProgress(int Done, int Total);

/// <summary>What a batch does when the destination name already exists (FEATURES §7).</summary>
public enum ConflictPolicy
{
    /// <summary>Leave the existing file alone and skip the item.</summary>
    Skip,

    /// <summary>Send the existing file to the Recycle Bin, then place the item.</summary>
    Replace,

    /// <summary>Place the item under a free name, e.g. <c>name 2.jpg</c>.</summary>
    KeepBoth,
}

/// <summary>Why one item of an operation could not be done.</summary>
public sealed class FileOperationException(FileOperationError error) : IOException(error.ToString())
{
    public FileOperationError Error { get; } = error;
}

public enum FileOperationError
{
    NameTaken,
    InvalidName,
    VerificationFailed,

    /// <summary>The Recycle Bin could not take the file (or no longer holds it).</summary>
    NotInRecycleBin,
}

/// <summary>
/// The file-system calls file operations make (macOS FileSystem), behind an interface so the safety tests can inject
/// failures, other volumes and a private Recycle Bin.
/// </summary>
public interface IFileSystem
{
    bool Exists(string path);

    /// <summary>An identifier of the volume holding <paramref name="path"/> (or its folder, if it does not exist yet).</summary>
    string? Volume(string path);

    /// <summary>Renames on one volume; fails when <paramref name="destination"/> exists (a case-only change of the same file is fine).</summary>
    void Move(string source, string destination);

    /// <summary>Copies a file; fails when <paramref name="destination"/> exists.</summary>
    void Copy(string source, string destination);

    /// <summary>
    /// Deletes for good. Used only for the operations' own temporary copies and for the source of a move to another
    /// volume after its copy was verified.
    /// </summary>
    void Remove(string path);

    /// <summary>Sends to the Recycle Bin; returns where the item lies there (for a restore).</summary>
    string Trash(string path);

    /// <summary>Takes an item out of the Recycle Bin (<paramref name="trashed"/>) to <paramref name="destination"/>.</summary>
    void Restore(string trashed, string destination);

    void CreateDirectory(string path);

    long? Size(string path);

    byte[]? QuickHash(string path);
}

/// <summary>Keeps the library index in step with file operations (asset ids survive moves and the Recycle Bin).</summary>
public interface IFileIndex
{
    Task MovedAsync(string source, string destination);

    /// <summary>Sent to the Recycle Bin (the asset becomes a tombstone).</summary>
    Task RemovedAsync(string path);

    /// <summary>Back from the Recycle Bin.</summary>
    Task RestoredAsync(string path);
}
