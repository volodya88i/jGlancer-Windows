namespace JGlancer.Core.FileOps;

/// <summary>
/// Runs file operations one item at a time (macOS FileOperationRunner, ARCHITECTURE §12). Same-volume moves are
/// renames; a move to another volume copies to a temporary name, verifies size and quick hash, places the copy, and
/// only then deletes the source. Copies also go through a temporary name, so a failure or a cancel never leaves a
/// partial file. Conflicts follow the batch's policy; replaced files go to the Recycle Bin. Completed items stay done
/// when a later one fails or the batch is cancelled; the result's undo record reverses exactly what was done.
/// One operation runs at a time.
/// </summary>
public sealed partial class FileOperationRunner(IFileSystem fileSystem, IFileIndex? index = null) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal IFileSystem FileSystem { get; } = fileSystem;

    public async Task<FileOperationResult> PerformAsync(
        FileOperation operation, ConflictPolicy policy = ConflictPolicy.KeepBoth,
        Action<FileProgress>? progress = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            return operation switch
            {
                FileOperation.Move move => await BatchAsync(UndoKind.Move, move.Sources, progress,
                    source => TransferAsync(source, move.Folder, policy, copying: false), cancellation).ConfigureAwait(false),
                FileOperation.Copy copy => await BatchAsync(UndoKind.Copy, copy.Sources, progress,
                    source => TransferAsync(source, copy.Folder, policy, copying: true), cancellation).ConfigureAwait(false),
                FileOperation.Rename rename => await BatchAsync(UndoKind.Rename, [rename.Source], progress,
                    source => RenameAsync(source, rename.Name), cancellation).ConfigureAwait(false),
                FileOperation.RenameAll all => await RenameAllAsync(all.Items, progress).ConfigureAwait(false),
                FileOperation.Trash trash => await BatchAsync(UndoKind.Trash, trash.Sources, progress, async source =>
                {
                    var undo = await RunAsync(new FileStep.Trash(source)).ConfigureAwait(false);
                    return ([undo], source);
                }, cancellation).ConfigureAwait(false),
                FileOperation.CreateFolder create => await BatchAsync(UndoKind.CreateFolder, [create.Parent], progress, async parent =>
                {
                    var folder = Vacant(Path.Combine(parent, create.Name));
                    var undo = await RunAsync(new FileStep.CreateFolder(folder)).ConfigureAwait(false);
                    return ([undo], folder);
                }, cancellation).ConfigureAwait(false),
                _ => throw new NotSupportedException(operation.GetType().Name),
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runs an undo (or redo) record; the result's record redoes (or undoes) it again.</summary>
    public async Task<FileOperationResult> PerformAsync(UndoRecord record, Action<FileProgress>? progress = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var result = new FileOperationResult(record.Kind);
            var inverses = new List<FileStep>();
            for (var number = 0; number < record.Steps.Count; number++)
            {
                if (cancellation.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    break;
                }
                var step = record.Steps[number];
                try
                {
                    inverses.Add(await RunAsync(step).ConfigureAwait(false));
                    result.Completed++;
                }
                catch (Exception error) when (IsFileError(error))
                {
                    result.Failures.Add(step.Subject);
                }
                progress?.Invoke(new FileProgress(number + 1, record.Steps.Count));
            }
            inverses.Reverse();
            result.Undo = new UndoRecord(record.Kind, inverses);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    // MARK: Batches

    /// <summary>Runs <paramref name="item"/> for each source; each returns its undo steps (in run order) and where the item is now (null: skipped).</summary>
    private static async Task<FileOperationResult> BatchAsync(
        UndoKind kind, IReadOnlyList<string> sources, Action<FileProgress>? progress,
        Func<string, Task<(List<FileStep> Undo, string? Location)>> item, CancellationToken cancellation)
    {
        var result = new FileOperationResult(kind);
        var inverses = new List<FileStep>();
        for (var number = 0; number < sources.Count; number++)
        {
            if (cancellation.IsCancellationRequested)
            {
                result.WasCancelled = true;
                break;
            }
            var source = sources[number];
            try
            {
                var (undo, location) = await item(source).ConfigureAwait(false);
                if (location is not null)
                {
                    inverses.AddRange(undo);
                    result.Completed++;
                    result.Results.Add(location);
                }
                else
                {
                    result.Skipped.Add(source);
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                result.Failures.Add(source);
            }
            progress?.Invoke(new FileProgress(number + 1, sources.Count));
        }
        inverses.Reverse();
        result.Undo = new UndoRecord(kind, inverses);
        return result;
    }

    /// <summary>Moves or copies one file into <paramref name="folder"/>, resolving a name conflict by <paramref name="policy"/>.</summary>
    private async Task<(List<FileStep>, string?)> TransferAsync(string source, string folder, ConflictPolicy policy, bool copying)
    {
        var destination = Path.Combine(folder, Path.GetFileName(source));
        if (!copying && FileNames.Same(destination, source))
        {
            return ([], null);
        }
        var undo = new List<FileStep>();
        if (FileSystem.Exists(destination))
        {
            switch (policy)
            {
                case ConflictPolicy.Skip:
                    return ([], null);
                case ConflictPolicy.KeepBoth:
                    destination = Vacant(destination);
                    break;
                case ConflictPolicy.Replace:
                    undo.Add(await RunAsync(new FileStep.Trash(destination)).ConfigureAwait(false));
                    break;
            }
        }
        undo.Add(await RunAsync(copying ? new FileStep.Copy(source, destination) : new FileStep.Move(source, destination)).ConfigureAwait(false));
        return (undo, destination);
    }

    internal async Task<(List<FileStep>, string?)> RenameAsync(string source, string name, string? originalName = null)
    {
        var trimmed = name.Trim();
        if (!FileNames.IsValid(trimmed))
        {
            throw new FileOperationException(FileOperationError.InvalidName);
        }
        var destination = Path.Combine(Path.GetDirectoryName(source) ?? "", trimmed);
        if (string.Equals(Path.GetFileName(destination), Path.GetFileName(source), StringComparison.Ordinal))
        {
            return ([], null);
        }
        // A case-only change of the same file is allowed; anything else that exists is taken.
        var caseOnly = string.Equals(trimmed, originalName ?? Path.GetFileName(source), StringComparison.OrdinalIgnoreCase);
        if (FileSystem.Exists(destination) && !caseOnly)
        {
            throw new FileOperationException(FileOperationError.NameTaken);
        }
        var undo = await RunAsync(new FileStep.Move(source, destination)).ConfigureAwait(false);
        return ([undo], destination);
    }

    // MARK: Steps

    /// <summary>Runs one step and returns its inverse.</summary>
    internal async Task<FileStep> RunAsync(FileStep step)
    {
        switch (step)
        {
            case FileStep.Move move:
                MoveFile(move.From, move.To);
                if (index is not null)
                {
                    await index.MovedAsync(move.From, move.To).ConfigureAwait(false);
                }
                return new FileStep.Move(move.To, move.From);
            case FileStep.Copy copy:
                CopyVerified(copy.From, copy.To);
                return new FileStep.Trash(copy.To);
            case FileStep.Trash trash:
                var trashed = FileSystem.Trash(trash.Path);
                if (index is not null)
                {
                    await index.RemovedAsync(trash.Path).ConfigureAwait(false);
                }
                return new FileStep.Restore(trashed, trash.Path);
            case FileStep.Restore restore:
                var destination = Vacant(restore.Original);
                FileSystem.Restore(restore.Trashed, destination);
                if (index is not null)
                {
                    await index.RestoredAsync(destination).ConfigureAwait(false);
                }
                return new FileStep.Trash(destination);
            case FileStep.CreateFolder create:
                FileSystem.CreateDirectory(create.Path);
                return new FileStep.Trash(create.Path);
            default:
                throw new NotSupportedException(step.GetType().Name);
        }
    }

    private void MoveFile(string source, string destination)
    {
        if (FileSystem.Exists(destination) && !FileNames.Same(source, destination))
        {
            throw new FileOperationException(FileOperationError.NameTaken);
        }
        var folder = Path.GetDirectoryName(destination) ?? destination;
        if (FileSystem.Volume(source) == FileSystem.Volume(folder))
        {
            FileSystem.Move(source, destination);
            return;
        }
        CopyVerified(source, destination);
        try
        {
            FileSystem.Remove(source);
        }
        catch (Exception error) when (IsFileError(error))
        {
            // Both copies exist: take the new one back so the move did not happen.
            try
            {
                FileSystem.Remove(destination);
            }
            catch (Exception cleanup) when (IsFileError(cleanup))
            {
            }
            throw;
        }
    }

    /// <summary>Copies through a temporary name next to the destination, checks size and quick hash, then places it.</summary>
    private void CopyVerified(string source, string destination)
    {
        if (FileSystem.Exists(destination))
        {
            throw new FileOperationException(FileOperationError.NameTaken);
        }
        var temporary = Path.Combine(Path.GetDirectoryName(destination) ?? "", $".jglancer-{Guid.NewGuid():N}-{Path.GetFileName(destination)}");
        FileSystem.Copy(source, temporary);
        if (FileSystem.Size(temporary) != FileSystem.Size(source)
            || FileSystem.QuickHash(temporary) is not { } copied || FileSystem.QuickHash(source) is not { } original
            || !copied.AsSpan().SequenceEqual(original))
        {
            TryRemove(temporary);
            throw new FileOperationException(FileOperationError.VerificationFailed);
        }
        try
        {
            FileSystem.Move(temporary, destination);
        }
        catch (Exception error) when (IsFileError(error))
        {
            TryRemove(temporary);
            throw;
        }
    }

    private void TryRemove(string path)
    {
        try
        {
            FileSystem.Remove(path);
        }
        catch (Exception error) when (IsFileError(error))
        {
        }
    }

    /// <summary><paramref name="path"/>, or "name 2.ext", … if taken.</summary>
    internal string Vacant(string path) => FileNames.Vacant(path, FileSystem.Exists);

    /// <summary>Failures of one item: the batch goes on.</summary>
    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    public void Dispose() => _gate.Dispose();
}
