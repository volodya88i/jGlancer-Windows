using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JGlancer.Core.Localization;

/// <summary>
/// One language's strings: the macOS catalog (<c>mac.xx.json</c>, generated from Localizable.xcstrings) overlaid by
/// Windows-only strings and Windows wording (<c>win.xx.json</c>, hand-written). Keys are the English source texts with
/// numbered placeholders, e.g. <c>"{0} items"</c>; a value is a string or an object of plural categories.
/// </summary>
public sealed class StringCatalog
{
    private readonly Dictionary<string, Entry> _entries;

    private StringCatalog(string language, Dictionary<string, Entry> entries)
    {
        Language = language;
        _entries = entries;
    }

    /// <summary>The two-letter language (<c>en</c>, <c>ru</c>).</summary>
    public string Language { get; }

    /// <summary>Every key this catalog has.</summary>
    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>Languages the app ships, base first.</summary>
    public static IReadOnlyList<string> Languages { get; } = ["en", "ru"];

    /// <summary>Loads a language from the resources embedded in this assembly.</summary>
    public static StringCatalog Load(string language)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var layer in new[] { "mac", "win" })
        {
            using var stream = typeof(StringCatalog).Assembly.GetManifestResourceStream($"Strings.{layer}.{language}.json");
            if (stream is not null)
            {
                Read(stream, entries);
            }
        }
        return new StringCatalog(language, entries);
    }

    /// <summary>Parses catalog JSON (for tests and tools).</summary>
    public static StringCatalog Parse(string language, string json)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), entries);
        return new StringCatalog(language, entries);
    }

    /// <summary>
    /// The translation of <paramref name="key"/>, or the key itself when missing. A key may carry a context after
    /// <c>|</c> to tell apart equal English words (<c>"Edit|menu"</c> vs the verb <c>"Edit"</c>); the context is
    /// never shown.
    /// </summary>
    public string Get(string key) =>
        _entries.TryGetValue(key, out var entry) ? entry.Text ?? entry.Plural("other") ?? Visible(key) : Visible(key);

    /// <summary>The key without its <c>|context</c> suffix.</summary>
    public static string Visible(string key)
    {
        var bar = key.IndexOf('|', StringComparison.Ordinal);
        return bar < 0 ? key : key[..bar];
    }

    /// <summary>The plural form of <paramref name="key"/> for <paramref name="count"/>, with placeholders filled.</summary>
    public string Plural(string key, long count, params object[] arguments)
    {
        string template;
        if (_entries.TryGetValue(key, out var entry) && entry.Text is null)
        {
            template = entry.Plural(PluralRules.Category(Language, count)) ?? entry.Plural("other") ?? key;
        }
        else
        {
            template = entry?.Text ?? Visible(key);
        }
        return Format(template, [count, .. arguments]);
    }

    /// <summary>Whether <paramref name="key"/> exists in this catalog.</summary>
    public bool Contains(string key) => _entries.ContainsKey(key);

    /// <summary>
    /// Replaces <c>{0}</c>…<c>{9}</c> only, so literal braces such as the batch-rename tokens <c>{name}</c> stay as they are.
    /// </summary>
    public static string Format(string template, params object?[] arguments)
    {
        var builder = new StringBuilder(template.Length + 16);
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] == '{' && i + 2 < template.Length && char.IsAsciiDigit(template[i + 1]) && template[i + 2] == '}')
            {
                var index = template[i + 1] - '0';
                if (index < arguments.Length)
                {
                    builder.Append(Convert.ToString(arguments[index], CultureInfo.CurrentCulture));
                    i += 2;
                    continue;
                }
            }
            builder.Append(template[i]);
        }
        return builder.ToString();
    }

    private static void Read(Stream stream, Dictionary<string, Entry> entries)
    {
        using var document = JsonDocument.Parse(stream);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                entries[property.Name] = new Entry(property.Value.GetString(), null);
            }
            else
            {
                var forms = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var form in property.Value.EnumerateObject())
                {
                    forms[form.Name] = form.Value.GetString() ?? "";
                }
                entries[property.Name] = new Entry(null, forms);
            }
        }
    }

    private sealed record Entry(string? Text, Dictionary<string, string>? Forms)
    {
        public string? Plural(string category) => Forms is not null && Forms.TryGetValue(category, out var form) ? form : null;
    }
}
