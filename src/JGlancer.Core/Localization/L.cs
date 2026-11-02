using System.Globalization;

namespace JGlancer.Core.Localization;

/// <summary>
/// The app's localized strings (EN base, RU). Every user-facing string goes through here; keys are the English
/// texts, e.g. <c>L.T("Add Folder…")</c>, <c>L.Plural("{0} items", count)</c>.
/// </summary>
public static class L
{
    private static StringCatalog _catalog = StringCatalog.Load("en");

    /// <summary>The active language.</summary>
    public static string Language => _catalog.Language;

    /// <summary>
    /// Chooses the language: an explicit <c>en</c> / <c>ru</c> (Settings → Language), or null for the system's UI
    /// language, falling back to English. Also sets the thread's UI culture for formatting and casing.
    /// </summary>
    public static void Use(string? language)
    {
        var chosen = language ?? CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        if (!StringCatalog.Languages.Contains(chosen))
        {
            chosen = "en";
        }
        _catalog = StringCatalog.Load(chosen);
        // Keep the user's own English variant (en-GB dates) when it is English; otherwise the language's main culture.
        var installed = CultureInfo.InstalledUICulture;
        var culture = installed.TwoLetterISOLanguageName == chosen
            ? installed
            : CultureInfo.GetCultureInfo(chosen == "ru" ? "ru-RU" : "en-US");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>A plain string, optionally with <c>{0}</c>-style arguments.</summary>
    public static string T(string key, params object?[] arguments) =>
        arguments.Length == 0 ? _catalog.Get(key) : StringCatalog.Format(_catalog.Get(key), arguments);

    /// <summary>A plural string; <paramref name="count"/> is <c>{0}</c>, further arguments follow.</summary>
    public static string Plural(string key, long count, params object[] arguments) =>
        _catalog.Plural(key, count, arguments);
}
