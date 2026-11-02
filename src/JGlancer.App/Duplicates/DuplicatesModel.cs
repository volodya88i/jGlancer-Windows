using JGlancer.App.FileOps;
using JGlancer.App.Services;
using JGlancer.Core.Model;
using JGlancer.Core.Scanning;

namespace JGlancer.App.Duplicates;

/// <summary>One set of copies: every file, and the ones ticked to go to the Recycle Bin (never all of them).</summary>
internal sealed class DuplicateGroup(IReadOnlyList<Asset> copies)
{
    public IReadOnlyList<Asset> Copies { get; } = copies;

    public HashSet<long> ToRemove { get; } = [];

    public long Size => Copies.Count > 0 ? Copies[0].Size : 0;

    /// <summary>Whether the copy can be ticked: one copy of the group always stays.</summary>
    public bool CanRemove(long id) => ToRemove.Contains(id) || ToRemove.Count < Copies.Count - 1;
}

/// <summary>
/// Find duplicates (W-78): the library's files with the same size and quick hash, in
/// groups, the extra copies ticked (the oldest copy stays). Removing proves each ticked copy byte for byte against a
/// copy that stays, then sends the proven ones to the Recycle Bin as one undoable operation.
/// </summary>
internal sealed class DuplicatesModel(AppServices services)
{
    public IReadOnlyList<DuplicateGroup> Groups { get; private set; } = [];

    public bool IsLoading { get; private set; }

    /// <summary>Files stage 2 has not read yet (no quick hash): duplicates among them are not found yet.</summary>
    public int Pending { get; private set; }

    public event EventHandler? Changed;

    public int ToRemoveCount => Groups.Sum(g => g.ToRemove.Count);

    public long ToRemoveBytes => Groups.Sum(g => g.ToRemove.Count * g.Size);

    public async Task LoadAsync()
    {
        IsLoading = true;
        Changed?.Invoke(this, EventArgs.Empty);
        var store = services.Store;
        var ids = await store.DuplicateGroupsAsync();
        var rows = new Dictionary<long, Asset>();
        foreach (var chunk in ids.SelectMany(g => g).Chunk(500))
        {
            foreach (var asset in await store.AssetsAsync(chunk))
            {
                rows[asset.Id] = asset;
            }
        }
        var groups = new List<DuplicateGroup>();
        foreach (var group in ids)
        {
            var copies = group.Where(rows.ContainsKey).Select(id => rows[id]).ToList();
            if (copies.Count < 2)
            {
                continue;
            }
            var result = new DuplicateGroup(copies);
            // The oldest copy (the original, most likely) stays; the others are ticked.
            var keep = copies.OrderBy(c => c.CreatedAt ?? c.ModifiedAt).ThenBy(c => c.ModifiedAt).First();
            result.ToRemove.UnionWith(copies.Where(c => c.Id != keep.Id).Select(c => c.Id));
            groups.Add(result);
        }
        Groups = groups;
        Pending = (await store.StatisticsAsync()).PendingMetadata;
        IsLoading = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetRemoved(DuplicateGroup group, long id, bool remove)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (remove && group.CanRemove(id))
        {
            group.ToRemove.Add(id);
        }
        else if (!remove)
        {
            group.ToRemove.Remove(id);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Proves and removes the ticked copies; returns how many went and how many turned out to differ (left alone).
    /// </summary>
    public async Task<(int Removed, int Different)> RemoveAsync(FileOperationsModel fileOps, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(fileOps);
        var proven = new List<string>();
        var different = 0;
        foreach (var group in Groups)
        {
            var kept = group.Copies.FirstOrDefault(c => !group.ToRemove.Contains(c.Id));
            if (kept is null)
            {
                continue;
            }
            foreach (var copy in group.Copies.Where(c => group.ToRemove.Contains(c.Id)))
            {
                if (await FileContent.SameAsync(copy.Path, kept.Path, cancellation))
                {
                    proven.Add(copy.Path);
                }
                else
                {
                    different++;
                }
            }
        }
        if (proven.Count > 0)
        {
            await fileOps.TrashAsync(proven);
        }
        return (proven.Count, different);
    }
}
