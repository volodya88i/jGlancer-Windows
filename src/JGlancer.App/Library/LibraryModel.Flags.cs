using Avalonia.Threading;
using JGlancer.App.FileOps;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>
/// Flags and search (macOS LibraryModel+Query, Flags; FEATURES §12a): the top bar's star filter, the search text with
/// its Everywhere switch, and flagging files, undoable through the app's history (<see cref="UndoableChanged"/>).
/// </summary>
internal sealed partial class LibraryModel
{
    private DispatcherTimer? _searchTimer;

    /// <summary>The text in the search field (the grid follows after a short pause in typing).</summary>
    public string SearchText { get; private set; } = "";

    /// <summary>Search the whole library instead of the folder or album shown.</summary>
    public bool SearchEverywhere { get; private set; }

    /// <summary>True while a search narrows the grid.</summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>Find (Ctrl+F): the top bar opens its field and takes the keyboard.</summary>
    public event EventHandler? SearchFocusRequested;

    /// <summary>A file's flag changed: stars outside the grid refresh (macOS FlagBook).</summary>
    public event EventHandler? FlagsChanged;

    public void SetFlagFilter(FlagFilter? filter)
    {
        if (filter == State.FlagFilter)
        {
            return;
        }
        UpdateState(s => s.FlagFilter = filter);
        ReloadQuery();
    }

    public void SetSearch(string text)
    {
        if (text == SearchText)
        {
            return;
        }
        var wasSearching = IsSearching;
        SearchText = text;
        StateChanged?.Invoke(this, EventArgs.Empty);
        _searchTimer?.Stop();
        if (!IsSearching && !wasSearching)
        {
            return;
        }
        _searchTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(JGMetrics.SearchDelay), DispatcherPriority.Normal, (_, _) =>
        {
            _searchTimer!.Stop();
            ReloadQuery();
        });
        _searchTimer.Start();
    }

    public void SetSearchEverywhere(bool everywhere)
    {
        if (everywhere == SearchEverywhere)
        {
            return;
        }
        SearchEverywhere = everywhere;
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (IsSearching)
        {
            ReloadQuery();
        }
    }

    /// <summary>Esc or × in the field: no search; the grid shows the folder or album again.</summary>
    public void EndSearch()
    {
        _searchTimer?.Stop();
        var wasSearching = IsSearching;
        SearchText = "";
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (wasSearching)
        {
            ReloadQuery();
        }
    }

    public void RequestSearchFocus() => SearchFocusRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Flags <paramref name="assetIds"/> with <paramref name="pressed"/>: if every one already has it, it comes off
    /// (<see cref="AssetFlags.Applying"/>). One undo step.
    /// </summary>
    /// <param name="continuesChord">
    /// The digit of a flag chord: the key before it already set the accent; this decides from the flags the files had
    /// before that key and takes the place of its undo step.
    /// </param>
    public async Task SetFlagAsync(AssetFlag pressed, IReadOnlyList<long> assetIds, bool continuesChord = false)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        if (assetIds.Count == 0)
        {
            return;
        }
        var chord = continuesChord ? _chord : null;
        var before = chord?.Before ?? await Store.FlagsAsync(assetIds);
        if (before.Count == 0)
        {
            return;
        }
        var flag = AssetFlags.Applying(pressed, [.. before.Values]);
        var previous = await Store.SetFlagAsync(flag, [.. before.Keys]);
        RaiseFlagsChanged();
        var undo = FlagRestoration(chord?.Before ?? previous, flag == AssetFlag.None ? L.T("Remove Flag") : L.T("Flag"));
        _chord = (chord?.Before ?? previous, undo);
        if (chord is { } last)
        {
            RaiseFlagUndoReplaced((last.Undo, undo));
        }
        else
        {
            RaiseUndoable(undo);
        }
    }

    /// <summary>The last flag change: the flags before it and its undo step (a chord digit replaces both).</summary>
    private (IReadOnlyDictionary<long, AssetFlag> Before, CustomUndo Undo)? _chord;

    /// <summary>A chord digit changed the flag just set: the window swaps the undo step.</summary>
    public event EventHandler<(CustomUndo Last, CustomUndo Change)>? FlagUndoReplaced;

    /// <summary>The flag of one file (viewers, the preview pane); none for files outside the library.</summary>
    public async Task<AssetFlag> FlagOfAsync(long assetId) =>
        (await Store.FlagsAsync([assetId])).GetValueOrDefault(assetId, AssetFlag.None);

    private CustomUndo FlagRestoration(IReadOnlyDictionary<long, AssetFlag> flags, string name) => new(name, async () =>
    {
        var now = await Store.FlagsAsync([.. flags.Keys]);
        await Store.RestoreFlagsAsync(flags);
        RaiseFlagsChanged();
        return FlagRestoration(now, name);
    });
}
