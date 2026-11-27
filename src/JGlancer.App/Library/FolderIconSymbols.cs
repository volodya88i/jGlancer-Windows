using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>
/// The symbols a library folder's icon can be (macOS D-129): the Lucide icons closest to the Mac's 48 SF Symbols, in its
/// order — drives and folders, places, media, music, marks, nature, travel, people and things. Stored by Lucide name.
/// </summary>
internal static class FolderIconSymbols
{
    public static IReadOnlyList<(string Name, string Data)> All { get; } =
    [
        ("hard-drive", JGIconData.HardDrive), ("server", JGIconData.Server), ("folder", JGIconData.Folder), ("inbox", JGIconData.Inbox),
        ("archive", JGIconData.Archive), ("package", JGIconData.Package), ("house", JGIconData.House), ("monitor", JGIconData.Monitor),
        ("laptop", JGIconData.Laptop), ("cloud", JGIconData.Cloud), ("image", JGIconData.Image), ("images", JGIconData.Images),
        ("camera", JGIconData.Camera), ("aperture", JGIconData.Aperture), ("film", JGIconData.Film), ("video", JGIconData.Video),
        ("tv", JGIconData.Tv), ("music", JGIconData.Music), ("list-music", JGIconData.ListMusic), ("headphones", JGIconData.Headphones),
        ("guitar", JGIconData.Guitar), ("piano", JGIconData.Piano), ("mic", JGIconData.Mic), ("audio-waveform", JGIconData.AudioWaveform),
        ("star", JGIconData.Star), ("heart", JGIconData.Heart), ("bookmark", JGIconData.Bookmark), ("tag", JGIconData.Tag),
        ("flag", JGIconData.Flag), ("zap", JGIconData.Zap), ("flame", JGIconData.Flame), ("sparkles", JGIconData.Sparkles),
        ("leaf", JGIconData.Leaf), ("sun", JGIconData.Sun), ("moon", JGIconData.Moon), ("cloud-sun", JGIconData.CloudSun),
        ("snowflake", JGIconData.Snowflake), ("plane", JGIconData.Plane), ("car", JGIconData.Car), ("globe", JGIconData.Globe),
        ("map", JGIconData.Map), ("user", JGIconData.User), ("users", JGIconData.Users), ("paw-print", JGIconData.PawPrint),
        ("gamepad-2", JGIconData.Gamepad2), ("palette", JGIconData.Palette), ("book", JGIconData.Book), ("gift", JGIconData.Gift),
    ];

    /// <summary>The path data of a stored symbol name; null for a name this app does not know.</summary>
    public static string? Data(string name) => All.FirstOrDefault(symbol => symbol.Name == name).Data;
}
