using JGlancer.Core.Model;

namespace JGlancer.App.Library;

/// <summary>How a sequence just changed in the grid: for its fly-out or fly-in (W-81).</summary>
internal sealed record SequenceToggle(ImageSequence Sequence, bool Expanding);

/// <summary>
/// Image sequences (W-81): frames named one after another fold into one stack in the grid — its first
/// frame, drawn as a pile of cards with the frame count — and unfold in place. The shown list (<see cref="Refs"/>)
/// leaves out the other frames of folded sequences; actions on a folded stack apply to all of its frames. Which
/// stacks are unfolded lasts while the app runs. Settings → Library turns the grouping off.
/// </summary>
internal sealed partial class LibraryModel
{
    private IReadOnlyList<AssetRef> _allRefs = [];
    private Dictionary<long, ImageSequence> _sequenceByHead = [];
    private readonly HashSet<long> _unfolded = [];

    /// <summary>The last fold or unfold, for the grid's animation; taken once.</summary>
    public SequenceToggle? TakeSequenceToggle()
    {
        var toggle = _lastToggle;
        _lastToggle = null;
        return toggle;
    }

    private SequenceToggle? _lastToggle;

    private static bool GroupsSequences => Settings.SettingsStore.Current.Settings.GroupSequences;

    /// <summary>The sequence whose first frame is <paramref name="id"/>; null for other files.</summary>
    public ImageSequence? SequenceOf(long id) => _sequenceByHead.GetValueOrDefault(id);

    /// <summary>True for the face of a folded stack.</summary>
    public bool IsFolded(long id) => _sequenceByHead.ContainsKey(id) && !_unfolded.Contains(id);

    /// <summary>Folds an unfolded sequence or unfolds a folded one (by its first frame).</summary>
    public void ToggleSequence(long head)
    {
        if (!_sequenceByHead.TryGetValue(head, out var sequence))
        {
            return;
        }
        var expanding = _unfolded.Add(head);
        if (!expanding)
        {
            _unfolded.Remove(head);
        }
        _lastToggle = new SequenceToggle(sequence, expanding);
        Publish();
    }

    /// <summary><paramref name="ids"/> with every frame of the folded stacks among them (actions act on all frames).</summary>
    public IReadOnlyList<long> WithSequenceFrames(IEnumerable<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var result = new List<long>();
        foreach (var id in ids)
        {
            if (IsFolded(id))
            {
                result.AddRange(_sequenceByHead[id].Frames);
            }
            else
            {
                result.Add(id);
            }
        }
        return result;
    }

    /// <summary>The query's files and their sequences arrived.</summary>
    private void SequencesArrived(IReadOnlyList<AssetRef> refs, IReadOnlyList<ImageSequence> sequences)
    {
        _allRefs = refs;
        _sequenceByHead = sequences.ToDictionary(s => s.Head);
        _unfolded.IntersectWith(_sequenceByHead.Keys);
        Publish();
    }

    /// <summary>The shown list: every file but the hidden frames of folded stacks.</summary>
    private void Publish()
    {
        var hidden = new HashSet<long>();
        foreach (var (head, sequence) in _sequenceByHead)
        {
            if (!_unfolded.Contains(head))
            {
                hidden.UnionWith(sequence.Frames.Skip(1));
            }
        }
        RefsArrived(hidden.Count == 0 ? _allRefs : [.. _allRefs.Where(r => !hidden.Contains(r.Id))]);
    }
}
