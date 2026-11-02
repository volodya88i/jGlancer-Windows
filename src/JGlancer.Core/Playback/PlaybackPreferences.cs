using System.Text.Json;
using System.Text.Json.Serialization;
using JGlancer.Core.Model;

namespace JGlancer.Core.Playback;

/// <summary>
/// Speed and "play next" per kind, volume and pitch preservation, remembered across launches (macOS
/// PlaybackPreferences, FEATURES §5). Keys missing from older files keep their defaults.
/// </summary>
public sealed record PlaybackPreferences
{
    public double VideoRate { get; set; } = PlaybackSpeed.Normal;

    public double AudioRate { get; set; } = PlaybackSpeed.Normal;

    /// <summary>0 … 1.</summary>
    public double Volume { get; set; } = 1;

    public bool PreservesPitch { get; set; } = true;

    /// <summary>Continue with the next file at the end: music does, a video stops (D-48, D-72).</summary>
    public bool VideoPlaysNext { get; set; }

    public bool AudioPlaysNext { get; set; } = true;

    /// <summary>Short fades at both ends of an exported audio segment (D-73), on by default.</summary>
    public bool SegmentFades { get; set; } = true;

    /// <summary>WAV, AIFF and FLAC segments keep their format (D-74; Settings in W9).</summary>
    public bool SegmentKeepsAudioFormat { get; set; } = true;

    public double Rate(MediaKind kind) => kind == MediaKind.Video ? VideoRate : AudioRate;

    public void SetRate(double rate, MediaKind kind)
    {
        if (kind == MediaKind.Video)
        {
            VideoRate = rate;
        }
        else
        {
            AudioRate = rate;
        }
    }

    public bool PlaysNext(MediaKind kind) => kind == MediaKind.Video ? VideoPlaysNext : AudioPlaysNext;

    public void SetPlaysNext(bool playsNext, MediaKind kind)
    {
        if (kind == MediaKind.Video)
        {
            VideoPlaysNext = playsNext;
        }
        else
        {
            AudioPlaysNext = playsNext;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, PlaybackPreferencesJson.Default.PlaybackPreferences);

    /// <summary>Reads saved preferences; anything unreadable gives the defaults (and out-of-range values are clamped).</summary>
    public static PlaybackPreferences FromJson(string? json)
    {
        PlaybackPreferences? read = null;
        try
        {
            read = json is null ? null : JsonSerializer.Deserialize(json, PlaybackPreferencesJson.Default.PlaybackPreferences);
        }
        catch (JsonException)
        {
        }
        var preferences = read ?? new PlaybackPreferences();
        preferences.VideoRate = PlaybackSpeed.Clamped(preferences.VideoRate);
        preferences.AudioRate = PlaybackSpeed.Clamped(preferences.AudioRate);
        preferences.Volume = Math.Clamp(preferences.Volume, 0, 1);
        return preferences;
    }
}

/// <summary>Source-generated JSON (NativeAOT: no reflection).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PlaybackPreferences))]
internal sealed partial class PlaybackPreferencesJson : JsonSerializerContext;
