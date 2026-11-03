using System.Globalization;

namespace JGlancer.Core.Playback;

/// <summary>
/// Reaching a pitch or a tempo by speed alone, as on a tape (W-108): the pitch and the tempo move together, nothing is
/// processed. At most an octave either way.
/// </summary>
public static class PlaybackTarget
{
    /// <summary>The largest distance from a song's own pitch, in semitones.</summary>
    public const int Limit = 12;

    private const double SlowestRate = 0.5;
    private const double FastestRate = 2;

    /// <summary>The rate that sounds <paramref name="semitones"/> away from the song's own pitch.</summary>
    public static double RateForSemitones(double semitones) => Math.Clamp(Math.Pow(2, semitones / 12), SlowestRate, FastestRate);

    /// <summary>The rate that takes a song of <paramref name="songBpm"/> to <paramref name="targetBpm"/>.</summary>
    public static double RateForBpm(double targetBpm, double songBpm) => Math.Clamp(targetBpm / songBpm, SlowestRate, FastestRate);

    /// <summary>How many semitones <paramref name="rate"/> moves the pitch.</summary>
    public static double Semitones(double rate) => 12 * Math.Log2(rate);

    /// <summary>Whether <paramref name="semitones"/> is close enough to a whole number to name a key.</summary>
    public static bool IsWhole(double semitones) => Math.Abs(semitones - Math.Round(semitones)) < 0.02;

    /// <summary>Reads a typed pitch: <c>5</c>, <c>+5</c>, <c>-3</c>, <c>−3</c>, <c>1.5</c>, <c>1,5</c>; kept within <see cref="Limit"/>.</summary>
    public static bool TryParseSemitones(string? text, out double semitones)
    {
        var read = TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, out semitones);
        semitones = Math.Clamp(semitones, -Limit, Limit);
        return read;
    }

    /// <summary>Reads a typed tempo: a positive number.</summary>
    public static bool TryParseBpm(string? text, out double bpm) =>
        TryParse(text, NumberStyles.AllowDecimalPoint, out bpm) && bpm > 0;

    /// <summary><c>+2</c>, <c>-3</c>, <c>+1.6</c>, <c>0</c>.</summary>
    public static string Label(double semitones)
    {
        var rounded = Math.Round(semitones, 1);
        return rounded.ToString(rounded > 0 ? "+0.#" : "0.#", CultureInfo.InvariantCulture);
    }

    private static bool TryParse(string? text, NumberStyles styles, out double value) =>
        double.TryParse(text?.Trim().Replace('−', '-').Replace(',', '.'), styles, CultureInfo.InvariantCulture, out value);
}
