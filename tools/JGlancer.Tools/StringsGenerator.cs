using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JGlancer.Tools;

/// <summary>
/// The macOS String Catalog (Localizable.xcstrings) → mac.en.json / mac.ru.json for <c>JGlancer.Core.Localization</c>.
/// Keys and values switch from printf specifiers (%@, %lld, %1$lld) to numbered placeholders ({0}, {1}).
/// Plural variations become objects with CLDR categories (one, few, many, other).
/// </summary>
internal static partial class StringsGenerator
{
    public static readonly string[] Languages = ["en", "ru"];

    public static IEnumerable<(string Language, string Json)> Generate(string xcstrings)
    {
        using var document = JsonDocument.Parse(xcstrings);
        var strings = document.RootElement.GetProperty("strings");
        var entries = strings.EnumerateObject()
            .Where(e => e.Name.Length > 0)
            .OrderBy(e => ConvertSpecifiers(e.Name), StringComparer.Ordinal)
            .ToList();
        foreach (var language in Languages)
        {
            var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
            {
                writer.WriteStartObject();
                foreach (var entry in entries)
                {
                    var key = ConvertSpecifiers(entry.Name);
                    writer.WritePropertyName(key);
                    WriteValue(writer, entry, language, key);
                }
                writer.WriteEndObject();
            }
            yield return (language, Encoding.UTF8.GetString(buffer.ToArray()) + "\n");
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonProperty entry, string language, string key)
    {
        if (!entry.Value.TryGetProperty("localizations", out var localizations)
            || !localizations.TryGetProperty(language, out var localization))
        {
            if (language != "en")
            {
                throw new InvalidDataException($"missing {language} translation for \"{entry.Name}\"");
            }
            writer.WriteStringValue(key);
            return;
        }
        if (localization.TryGetProperty("stringUnit", out var unit))
        {
            writer.WriteStringValue(ConvertSpecifiers(unit.GetProperty("value").GetString()!));
            return;
        }
        var plural = localization.GetProperty("variations").GetProperty("plural");
        writer.WriteStartObject();
        foreach (var category in plural.EnumerateObject())
        {
            writer.WriteString(category.Name,
                ConvertSpecifiers(category.Value.GetProperty("stringUnit").GetProperty("value").GetString()!));
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// <c>%@</c>, <c>%lld</c>, <c>%d</c> → <c>{0}</c>, <c>{1}</c> in order; <c>%2$@</c> → <c>{1}</c>; <c>%%</c> → <c>%</c>.
    /// </summary>
    internal static string ConvertSpecifiers(string text)
    {
        var next = 0;
        return Specifier().Replace(text, match =>
        {
            if (match.Value == "%%")
            {
                return "%";
            }
            var index = match.Groups["pos"].Success ? int.Parse(match.Groups["pos"].Value, System.Globalization.CultureInfo.InvariantCulture) - 1 : next++;
            return $"{{{index}}}";
        });
    }

    [GeneratedRegex(@"%%|%(?:(?<pos>\d+)\$)?(?:lld|llu|ld|lu|d|u|@|f|\.\d+f)")]
    private static partial Regex Specifier();
}
