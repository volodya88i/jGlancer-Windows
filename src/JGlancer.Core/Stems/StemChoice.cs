namespace JGlancer.Core.Stems;

/// <summary>
/// The split window's choice: the mode, the stems ticked and, for 2 stems, the quality (remembered between uses, not
/// the folder). At least one stem of the mode must be ticked. <paramref name="HighQuality"/>: 2 stems with Mel-Band
/// RoFormer instead of the 4-stem Demucs: cleaner, a larger download, slower (MAC-056).
/// </summary>
public sealed record StemChoice(StemMode Mode, IReadOnlySet<StemKind> Ticked, bool HighQuality = false)
{
    /// <summary>4 stems, all ticked.</summary>
    public static StemChoice Default { get; } = new(StemMode.Four, new HashSet<StemKind>(StemModelInfo.Shown(StemMode.Four)));

    /// <summary>The ticked stems of the mode, in the window's order.</summary>
    public IReadOnlyList<StemKind> Stems => [.. StemModelInfo.Shown(Mode).Where(Ticked.Contains)];

    public bool CanSplit => Stems.Count > 0;

    /// <summary>The network a split with this choice runs: High Quality counts for 2 stems only.</summary>
    public StemNetwork Network => Mode == StemMode.Two && HighQuality ? StemNetwork.Vocals : Mode.Network();

    /// <summary>
    /// Another mode: the stems both modes have keep their ticks; the new mode's own stems (guitar, piano, the
    /// instrumental) come ticked, and so does everything when nothing would be left ticked.
    /// </summary>
    public StemChoice WithMode(StemMode mode)
    {
        if (mode == Mode)
        {
            return this;
        }
        var old = StemModelInfo.Shown(Mode);
        var ticked = StemModelInfo.Shown(mode).Where(stem => !old.Contains(stem) || Ticked.Contains(stem)).ToHashSet();
        return this with { Mode = mode, Ticked = ticked.Count > 0 ? ticked : [.. StemModelInfo.Shown(mode)] };
    }

    /// <summary>Ticks or unticks a stem; the last ticked one stays ticked.</summary>
    public StemChoice With(StemKind stem, bool ticked)
    {
        var set = new HashSet<StemKind>(Ticked);
        if (ticked)
        {
            set.Add(stem);
        }
        else
        {
            set.Remove(stem);
        }
        var result = this with { Ticked = set };
        return result.CanSplit ? result : this;
    }
}
