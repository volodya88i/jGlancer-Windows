using JGlancer.Core.Model;

namespace JGlancer.Core.Playback;

/// <summary>One playable file of a queue: a library asset (or, later, a file opened from outside the library).</summary>
/// <param name="Path">The file; also the item's identity.</param>
/// <param name="AssetId">Set for library assets (selection on return, cached thumbnails).</param>
/// <param name="Duration">Seconds, when the library already knows it.</param>
public sealed record PlaybackItem(
    string Path,
    string Name,
    MediaKind Kind,
    long? AssetId = null,
    DateTimeOffset? ModifiedAt = null,
    string? TypeIdentifier = null,
    double? Duration = null)
{
    /// <summary>A library asset as a queue item.</summary>
    public static PlaybackItem From(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new PlaybackItem(asset.Path, asset.Name, asset.Kind, asset.Id, asset.ModifiedAt, asset.TypeIdentifier, asset.Duration);
    }
}

/// <summary>What happens when the current item plays to its end.</summary>
public abstract record EndAction
{
    /// <summary>Start the current item again (loop).</summary>
    public sealed record Replay : EndAction;

    /// <summary>Move to the item at <paramref name="Index"/> and play it.</summary>
    public sealed record Advance(int Index) : EndAction;

    /// <summary>Stay at the end, paused.</summary>
    public sealed record StayAtEnd : EndAction;
}

/// <summary>What "previous" does.</summary>
public abstract record PreviousAction
{
    public sealed record Restart : PreviousAction;

    /// <summary>Go to the item at <paramref name="Index"/>.</summary>
    public sealed record Move(int Index) : PreviousAction;
}

/// <summary>
/// The files a player moves through: the videos or the audio files of one folder (macOS PlaybackQueue, D-09), and the
/// rules for previous, next and the end of an item. No wrap-around at the ends. Immutable: changes return a new queue.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "The macOS name; it is a queue of files to play.")]
public sealed record PlaybackQueue
{
    /// <summary>Seconds into an item after which "previous" restarts it instead of going back.</summary>
    public const double RestartThreshold = 3;

    public PlaybackQueue(IReadOnlyList<PlaybackItem>? items = null, int index = 0)
    {
        Items = items ?? [];
        Index = Items.Count == 0 ? 0 : Math.Clamp(index, 0, Items.Count - 1);
    }

    public static PlaybackQueue Empty { get; } = new();

    public IReadOnlyList<PlaybackItem> Items { get; }

    public int Index { get; }

    public PlaybackItem? Current => Index < Items.Count ? Items[Index] : null;

    public int Count => Items.Count;

    public bool HasNext => Index + 1 < Items.Count;

    public bool HasPrevious => Index > 0;

    /// <summary>The queue with <paramref name="index"/> current, or null when it is out of range or already current.</summary>
    public PlaybackQueue? Select(int index) =>
        index < 0 || index >= Items.Count || index == Index ? null : new PlaybackQueue(Items, index);

    /// <summary><paramref name="loop"/> repeats the current item; <paramref name="autoAdvance"/> continues with the next.</summary>
    public EndAction EndAction(bool loop, bool autoAdvance) =>
        loop ? new EndAction.Replay() : autoAdvance && HasNext ? new EndAction.Advance(Index + 1) : new EndAction.StayAtEnd();

    /// <summary>Restarts the current item when playback is past <see cref="RestartThreshold"/> or nothing is before it.</summary>
    public PreviousAction PreviousAction(double currentTime) =>
        currentTime > RestartThreshold || !HasPrevious ? new PreviousAction.Restart() : new PreviousAction.Move(Index - 1);

    /// <summary>A queue of <paramref name="items"/> with the item at <paramref name="path"/> current, if it is among them.</summary>
    public static PlaybackQueue Keeping(IReadOnlyList<PlaybackItem> items, string? path)
    {
        ArgumentNullException.ThrowIfNull(items);
        var index = 0;
        for (var i = 0; path is not null && i < items.Count; i++)
        {
            if (string.Equals(items[i].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        return new PlaybackQueue(items, index);
    }

    /// <summary>The queue without the current item (its file went to the Trash): the next one, or the previous at the end.</summary>
    public PlaybackQueue RemovingCurrent()
    {
        if (Current is null)
        {
            return this;
        }
        var items = Items.Where((_, i) => i != Index).ToList();
        return new PlaybackQueue(items, Math.Min(Index, items.Count - 1));
    }
}

/// <summary>
/// Playback rate presets, range and stepping (macOS PlaybackSpeed, FEATURES §5, D-17): 0.25×–4×, presets 0.5–2×,
/// `[` `]` move by 0.05.
/// </summary>
public static class PlaybackSpeed
{
    public const double Normal = 1;

    /// <summary>Increment of the <c>[</c> and <c>]</c> shortcuts.</summary>
    public const double FineStep = 0.05;

    public const double Minimum = 0.25;

    public const double Maximum = 4;

    /// <summary>Rates offered in the speed menu, ascending.</summary>
    public static IReadOnlyList<double> Presets { get; } = [0.5, 0.75, 1, 1.25, 1.5, 2];

    /// <summary><paramref name="rate"/> inside the range, rounded to the fine step (repeated steps never drift).</summary>
    public static double Clamped(double rate) => Math.Clamp(Math.Round(Math.Round(rate / FineStep) * FineStep, 2), Minimum, Maximum);

    /// <summary><paramref name="rate"/> moved by <paramref name="steps"/> fine steps (negative slows down), clamped.</summary>
    public static double Stepped(double rate, int steps) => Clamped(rate + steps * FineStep);

    /// <summary>The presets, plus <paramref name="current"/> when the keys set a speed in between.</summary>
    public static IReadOnlyList<double> Choices(double current) =>
        Presets.Contains(current) ? Presets : [.. Presets.Append(current).Order()];
}
