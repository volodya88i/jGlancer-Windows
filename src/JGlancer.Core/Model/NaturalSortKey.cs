using System.Globalization;
using System.Text;

namespace JGlancer.Core.Model;

/// <summary>
/// Builds a key whose ordinal order equals natural file-name order (macOS NaturalSortKey). Case, diacritics and
/// width are folded, and every run of ASCII digits is left-padded to a fixed width, so "img_2.jpg" sorts before
/// "IMG_10.jpg". Stored in <c>asset.name_key</c> so SQLite sorts with an index instead of a custom collation.
/// </summary>
public static class NaturalSortKey
{
    /// <summary>Digit runs are padded to this many characters; longer runs keep their length.</summary>
    public const int DigitWidth = 12;

    /// <summary>The sort key for <paramref name="name"/>.</summary>
    public static string Make(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var folded = Fold(name);
        var key = new StringBuilder(folded.Length + DigitWidth);
        var digits = new StringBuilder();
        void FlushDigits()
        {
            if (digits.Length == 0)
            {
                return;
            }
            var significant = digits.ToString().TrimStart('0');
            if (significant.Length == 0)
            {
                significant = "0";
            }
            if (significant.Length < DigitWidth)
            {
                key.Append('0', DigitWidth - significant.Length);
            }
            key.Append(significant);
            digits.Clear();
        }
        foreach (var c in folded)
        {
            if (char.IsAsciiDigit(c))
            {
                digits.Append(c);
            }
            else
            {
                FlushDigits();
                key.Append(c);
            }
        }
        FlushDigits();
        return key.ToString();
    }

    /// <summary>Compares two names in natural order.</summary>
    public static int Compare(string a, string b) => string.CompareOrdinal(Make(a), Make(b));

    /// <summary>Compatibility decomposition (folds width), combining marks dropped, invariant lower case.</summary>
    private static string Fold(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
