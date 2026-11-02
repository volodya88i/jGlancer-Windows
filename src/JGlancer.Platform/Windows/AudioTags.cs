using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>
/// A song's title, artist and album from the Shell property system, before the player opens it (the audio viewer's
/// neighbor while swiping and the first frame of a new song). Formats without a handler give nothing.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class AudioTags
{
    private static readonly PropertyKey? TitleKey = Key("System.Title");
    private static readonly PropertyKey? ArtistKey = Key("System.Music.Artist");
    private static readonly PropertyKey? AlbumKey = Key("System.Music.AlbumTitle");

    public sealed record Tags(string? Title, string? Artist, string? Album);

    /// <summary>Reads <paramref name="path"/> (blocking: call it off the UI thread); null without a property handler.</summary>
    public static Tags? Read(string path)
    {
        var store = ComInterop.PropertyStore(path);
        if (store is null)
        {
            return null;
        }
        try
        {
            static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            var artists = Value(store, ArtistKey, v => v.Strings ?? (v.Text is { } one ? [one] : null));
            return new Tags(
                Clean(Value(store, TitleKey, v => v.Text)),
                Clean(artists is { Length: > 0 } ? string.Join(", ", artists) : null),
                Clean(Value(store, AlbumKey, v => v.Text)));
        }
        finally
        {
            ComInterop.Release(store);
        }
    }

    private static T? Value<T>(IPropertyStore store, PropertyKey? optionalKey, Func<PropVariant, T?> read)
    {
        if (optionalKey is not { } key)
        {
            return default;
        }
        var value = default(PropVariant);
        try
        {
            return store.GetValue(&key, &value) >= 0 ? read(value) : default;
        }
        finally
        {
            value.Clear();
        }
    }

    private static PropertyKey? Key(string name)
    {
        PropertyKey key;
        return PSGetPropertyKeyFromName(name, &key) >= 0 ? key : null;
    }

    [LibraryImport("propsys.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PSGetPropertyKeyFromName(string name, PropertyKey* key);
}
