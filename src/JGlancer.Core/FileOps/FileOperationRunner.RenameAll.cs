namespace JGlancer.Core.FileOps;

/// <summary>Batch rename (<see cref="FileOperation.RenameAll"/>; macOS FileOperationRunner+BatchRename).</summary>
public sealed partial class FileOperationRunner
{
    /// <summary>
    /// Every name is checked before anything moves: an invalid or duplicate name, or one another file in the folder
    /// has, fails that item, and then every item that wanted the failed file's name fails too. Files whose name
    /// another file of the batch takes first move to a temporary name (so swaps and shifts work), then every file gets
    /// its new name. Should a move still fail, the file returns to its old name, or a free one next to it, never
    /// staying under the temporary one. One undo record covers everything done.
    /// </summary>
    private async Task<FileOperationResult> RenameAllAsync(IReadOnlyList<(string Source, string Name)> items, Action<FileProgress>? progress)
    {
        var result = new FileOperationResult(UndoKind.Rename);
        var targets = items.Select(item => Path.Combine(Path.GetDirectoryName(item.Source) ?? "", item.Name.Trim())).ToList();
        var failed = RenameFailures(items, targets);
        var steps = new List<FileStep>();
        var current = items.Select(item => item.Source).ToList();
        var wanted = new HashSet<string>(
            Enumerable.Range(0, items.Count).Where(i => !failed.Contains(i)).Select(i => targets[i]), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++)
        {
            if (failed.Contains(i) || !wanted.Contains(current[i]) || FileNames.Same(targets[i], current[i]))
            {
                continue;
            }
            var temporary = Path.Combine(Path.GetDirectoryName(current[i]) ?? "", $".jglancer-rename-{Guid.NewGuid():N}");
            try
            {
                steps.Add(await RunAsync(new FileStep.Move(current[i], temporary)).ConfigureAwait(false));
                current[i] = temporary;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        for (var i = 0; i < items.Count; i++)
        {
            var source = items[i].Source;
            if (failed.Contains(i))
            {
                result.Failures.Add(source);
                progress?.Invoke(new FileProgress(i + 1, items.Count));
                continue;
            }
            try
            {
                var (undo, location) = await RenameAsync(current[i], items[i].Name, Path.GetFileName(source)).ConfigureAwait(false);
                steps.AddRange(undo);
                if (location is not null)
                {
                    result.Completed++;
                    result.Results.Add(location);
                }
                else
                {
                    result.Skipped.Add(source);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (!FileNames.Same(current[i], source))
                {
                    try
                    {
                        steps.Add(await RunAsync(new FileStep.Move(current[i], Vacant(source))).ConfigureAwait(false));
                    }
                    catch (Exception back) when (back is IOException or UnauthorizedAccessException)
                    {
                    }
                }
                result.Failures.Add(source);
            }
            progress?.Invoke(new FileProgress(i + 1, items.Count));
        }
        steps.Reverse();
        result.Undo = new UndoRecord(UndoKind.Rename, steps);
        return result;
    }

    /// <summary>Items of a batch rename that cannot be done (see <see cref="RenameAllAsync"/>).</summary>
    private HashSet<int> RenameFailures(IReadOnlyList<(string Source, string Name)> items, List<string> targets)
    {
        var sources = new HashSet<string>(items.Select(item => item.Source), StringComparer.OrdinalIgnoreCase);
        var counts = targets.GroupBy(t => t, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var failed = Enumerable.Range(0, items.Count).Where(i =>
            !FileNames.IsValid(items[i].Name.Trim())
            || counts[targets[i]] > 1
            || (FileSystem.Exists(targets[i]) && !sources.Contains(targets[i]))).ToHashSet();
        // A file that stays keeps its name: whoever wanted that name fails too.
        while (true)
        {
            var kept = new HashSet<string>(failed.Select(i => items[i].Source), StringComparer.OrdinalIgnoreCase);
            var more = Enumerable.Range(0, items.Count)
                .Where(i => !failed.Contains(i) && kept.Contains(targets[i]) && !FileNames.Same(targets[i], items[i].Source))
                .ToList();
            if (more.Count == 0)
            {
                return failed;
            }
            failed.UnionWith(more);
        }
    }
}
