namespace JGlancer.Core.Audio;

/// <summary>A musical key: <see cref="Tonic"/> is the pitch class, 0 = C … 11 = B.</summary>
public readonly record struct MusicKey(int Tonic, bool IsMinor)
{
    private static readonly string[] Names = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    /// <summary><c>A min</c>, <c>Eb maj</c>.</summary>
    public string Label => $"{Names[Tonic]} {(IsMinor ? "min" : "maj")}";

    /// <summary>The key <paramref name="semitones"/> higher (lower when negative).</summary>
    public MusicKey Shifted(int semitones) => this with { Tonic = ((Tonic + semitones) % 12 + 12) % 12 };
}

/// <summary>What the analysis found in a song; either part may be missing (silence, no steady beat).</summary>
public sealed record AudioAnalysis(double? Bpm, MusicKey? Key)
{
    /// <summary>The tempo as shown: whole beats per minute.</summary>
    public string? BpmLabel => Bpm is { } bpm ? Math.Round(bpm).ToString("0", System.Globalization.CultureInfo.InvariantCulture) : null;
}
