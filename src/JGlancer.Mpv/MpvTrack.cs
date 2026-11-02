namespace JGlancer.Mpv;

/// <summary>
/// One stream of the current file as mpv lists it (W-88): <paramref name="Type"/> is "video", "audio" or "sub";
/// <paramref name="IsExternal"/> for a subtitle file beside the video.
/// </summary>
public sealed record MpvTrack(long Id, string Type, string? Title, string? Language, string? Codec, bool IsDefault, bool IsExternal, int Channels);
