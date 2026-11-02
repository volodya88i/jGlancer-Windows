namespace JGlancer.Core.Localization;

/// <summary>CLDR plural categories for the app's languages (cardinal, integers only).</summary>
public static class PluralRules
{
    /// <summary>The category name (<c>one</c>, <c>few</c>, <c>many</c>, <c>other</c>) of <paramref name="count"/>.</summary>
    public static string Category(string language, long count)
    {
        var n = Math.Abs(count);
        return language switch
        {
            "ru" => Russian(n),
            _ => n == 1 ? "one" : "other",
        };
    }

    private static string Russian(long n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11)
        {
            return "one";
        }
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
        {
            return "few";
        }
        return "many";
    }
}
