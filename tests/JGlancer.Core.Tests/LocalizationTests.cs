using JGlancer.Core.Localization;
using JGlancer.Tools;

namespace JGlancer.Core.Tests;

[Collection("Localization")]
public sealed class LocalizationTests
{
    [Theory]
    [InlineData(0, "many")]
    [InlineData(1, "one")]
    [InlineData(2, "few")]
    [InlineData(4, "few")]
    [InlineData(5, "many")]
    [InlineData(11, "many")]
    [InlineData(12, "many")]
    [InlineData(14, "many")]
    [InlineData(21, "one")]
    [InlineData(22, "few")]
    [InlineData(111, "many")]
    [InlineData(1001, "one")]
    public void RussianPluralCategories(long count, string expected) =>
        Assert.Equal(expected, PluralRules.Category("ru", count));

    [Theory]
    [InlineData(1, "one")]
    [InlineData(0, "other")]
    [InlineData(2, "other")]
    public void EnglishPluralCategories(long count, string expected) =>
        Assert.Equal(expected, PluralRules.Category("en", count));

    [Fact]
    public void BothLanguagesLoadFromEmbeddedResources()
    {
        var english = StringCatalog.Load("en");
        var russian = StringCatalog.Load("ru");
        Assert.True(english.Keys.Count() > 250);
        Assert.Equal("Добавить папку…", russian.Get("Add Folder…"));
        Assert.Equal("Add Folder…", english.Get("Add Folder…"));
    }

    [Fact]
    public void EveryEnglishKeyHasARussianTranslation()
    {
        var russian = StringCatalog.Load("ru");
        var missing = StringCatalog.Load("en").Keys.Where(key => !russian.Contains(key)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void PluralsFillTheCount()
    {
        var russian = StringCatalog.Load("ru");
        Assert.Equal("1 файл", russian.Plural("{0} items", 1));
        Assert.Equal("3 файла", russian.Plural("{0} items", 3));
        Assert.Equal("25 файлов", russian.Plural("{0} items", 25));
        Assert.Equal("2 items", StringCatalog.Load("en").Plural("{0} items", 2));
    }

    [Fact]
    public void ContextIsNeverShown()
    {
        Assert.Equal("Правка", StringCatalog.Load("ru").Get("Edit|menu"));
        Assert.Equal("Редактировать", StringCatalog.Load("ru").Get("Edit"));
        Assert.Equal("Unknown", StringCatalog.Load("en").Get("Unknown|context"));
    }

    [Fact]
    public void FormatReplacesOnlyNumberedPlaceholders()
    {
        Assert.Equal("Trip {n} of 3 — {name}", StringCatalog.Format("Trip {n} of {0} — {name}", 3));
        Assert.Equal("{1} stays", StringCatalog.Format("{1} stays", "only one"));
    }

    [Fact]
    public void UseSwitchesTheActiveLanguage()
    {
        L.Use("ru");
        try
        {
            Assert.Equal("ru", L.Language);
            Assert.Equal("Библиотека", L.T("Library"));
        }
        finally
        {
            L.Use("en");
        }
    }

    [Fact]
    public void ByteSizesReadLikeMacOS()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            L.Use("en");
            Assert.Equal("Zero KB", ByteFormat.Format(0));
            Assert.Equal("1 byte", ByteFormat.Format(1));
            Assert.Equal("512 bytes", ByteFormat.Format(512));
            Assert.Equal("203 KB", ByteFormat.Format(203_400));
            Assert.Equal("140.3 MB", ByteFormat.Format(140_312_000));
            Assert.Equal("2 MB", ByteFormat.Format(2_000_000));
            Assert.Equal("1 GB", ByteFormat.Format(999_990_000));
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            L.Use("ru");
            Assert.Equal("140,3 МБ", ByteFormat.Format(140_312_000));
            Assert.Equal("3 байта", ByteFormat.Format(3));
        }
        finally
        {
            L.Use("en");
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("%lld items", "{0} items")]
    [InlineData("%@ (default)", "{0} (default)")]
    [InlineData("%1$lld of %2$lld selected", "{0} of {1} selected")]
    [InlineData("%2$@ and %1$@", "{1} and {0}")]
    [InlineData("100%% done", "100% done")]
    public void SpecifiersBecomeNumberedPlaceholders(string source, string expected) =>
        Assert.Equal(expected, StringsGenerator.ConvertSpecifiers(source));
}
