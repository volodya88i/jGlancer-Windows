using System.Globalization;

namespace JGlancer.Core.Model;

/// <summary>
/// A library folder's own icon (macOS D-129), shown in the sidebar and on its strip instead of the disk: a symbol (a
/// Lucide icon by name on Windows), an emoji, or a small square picture made once from a photo (the photo itself is
/// never changed nor needed later).
/// </summary>
public abstract record FolderIcon
{
    private FolderIcon()
    {
    }

    /// <summary>The side of a picture icon in pixels: sharp at the sidebar's size on a high-density screen, small in the library.</summary>
    public const int PictureSide = 96;

    public sealed record Symbol(string Name) : FolderIcon;

    public sealed record Emoji(string Text) : FolderIcon;

    /// <summary>JPEG data, square, <see cref="PictureSide"/> pixels.</summary>
    public sealed record Picture(byte[] Jpeg) : FolderIcon;

    /// <summary>
    /// The last emoji in <paramref name="text"/> (typed or picked from Windows' emoji panel), or null. An emoji is a
    /// character shown as a picture: one past U+1F000 or among the symbols U+2300–27BF and U+2B00–2BFF, or a sequence
    /// with the picture selector (U+FE0F), a keycap (U+20E3) or a joiner (U+200D); a digit or # alone is not one.
    /// </summary>
    public static string? LastEmoji(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        string? last = null;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (IsEmoji(element))
            {
                last = element;
            }
        }
        return last;
    }

    private static bool IsEmoji(string element)
    {
        foreach (var rune in element.EnumerateRunes())
        {
            var value = rune.Value;
            if (value >= 0x1F000 || value is >= 0x2300 and <= 0x27BF or >= 0x2B00 and <= 0x2BFF or 0xFE0F or 0x20E3 or 0x200D)
            {
                return true;
            }
        }
        return false;
    }
}
