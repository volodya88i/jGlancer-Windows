using JGlancer.Core.Actions;
using JGlancer.Core.Model;

namespace JGlancer.App.Actions;

/// <summary>The files an action applies to (macOS ActionTarget): the library selection or the file in a viewer.</summary>
/// <param name="AssetIds">Library assets in display order.</param>
/// <param name="Sample">One file known at once (the clicked or focused one): the Open With suggestions are for its type.</param>
/// <param name="Paths">Every file's path; library paths come from the store when an action runs.</param>
internal sealed record ActionTarget(
    ActionPlace Place,
    IReadOnlyList<long> AssetIds,
    IReadOnlySet<MediaKind> Kinds,
    string? Sample,
    Func<Task<IReadOnlyList<string>>> Paths)
{
    public int Count => AssetIds.Count;

    /// <summary>The albums each file is in (Add to Album marks "already added"); loaded with the context menu.</summary>
    public IReadOnlyDictionary<long, IReadOnlySet<long>>? Memberships { get; init; }

    public ActionSelection Selection => new(Count, Kinds, Place);

    /// <summary>The one kind of the files, if they share it.</summary>
    public MediaKind? Kind => Kinds.Count == 1 ? Kinds.First() : null;
}
