using JGlancer.Core.Actions;
using JGlancer.Core.Settings;

namespace JGlancer.Core.Tests.Settings;

/// <summary>Ports of the macOS AppSettingsTests, AppearancePresetTests and ShortcutMapTests.</summary>
public sealed class SettingsTests
{
    [Fact]
    public void DefaultsArePerformanceAndDark()
    {
        var settings = new AppSettings();
        Assert.Equal(AppearancePreset.Performance, AppearancePresets.Matching(settings));
        Assert.Equal(AppTheme.Dark, settings.Theme);
        Assert.False(settings.HasChosenAppearance);
    }

    [Fact]
    public void PresetsSetOnlyTheirOptionsAndAHandMixMatchesNone()
    {
        var beauty = AppearancePreset.Beauty.Applied(new AppSettings { Theme = AppTheme.Light, CrosshairMarks = true });
        Assert.True(beauty.CoverBackdrop);
        Assert.Equal(FilmstripReveal.Automatic, beauty.FilmstripReveal);
        Assert.True(beauty.SwipeAnimation);
        Assert.True(beauty.GridAnimation);
        Assert.Equal(AppTheme.Light, beauty.Theme);
        Assert.True(beauty.CrosshairMarks);
        Assert.Equal(AppearancePreset.Beauty, AppearancePresets.Matching(beauty));
        Assert.Null(AppearancePresets.Matching(beauty with { SwipeAnimation = false }));
    }

    [Fact]
    public void MissingKeysKeepTheirDefaultsAndBadValuesAreBroughtBack()
    {
        var read = AppSettings.FromJson("""{ "theme": "light", "thumbnailCacheGigabytes": 3 }""");
        Assert.Equal(AppTheme.Light, read.Theme);
        Assert.True(read.AutoplayInPreview);
        Assert.Equal(2, read.ThumbnailCacheGigabytes);
        Assert.Equal(new AppSettings(), AppSettings.FromJson("{ not json"));
        var saved = new AppSettings { Language = AppLanguage.Russian, KindFilterStyle = KindFilterStyle.Icons, HasChosenAppearance = true };
        Assert.Equal(saved, AppSettings.FromJson(saved.ToJson()));
    }

    [Fact]
    public void AShortcutMovesOverAndPlainKeysAreRefused()
    {
        var map = new ShortcutMap();
        Assert.Equal(new ActionShortcut("R", ActionModifiers.Control | ActionModifiers.Alt), map.Shortcut(ShortcutCommand.ShowInExplorer));
        var taken = new ActionShortcut("E", ActionModifiers.Control | ActionModifiers.Alt);
        Assert.Equal(ShortcutMap.Outcome.Assigned, map.Assign(taken, ShortcutCommand.CopyPath, out var from));
        Assert.Equal(ShortcutCommand.EditInPrimaryApp, from);
        Assert.Null(map.Shortcut(ShortcutCommand.EditInPrimaryApp));
        Assert.Equal(ShortcutCommand.CopyPath, map.CommandFor(taken));
        Assert.Equal(ShortcutMap.Outcome.NeedsControlOrAlt, map.Assign(new ActionShortcut("K"), ShortcutCommand.CopyTo, out _));
        Assert.Equal(ShortcutMap.Outcome.NeedsControlOrAlt, map.Assign(new ActionShortcut("K", ActionModifiers.Shift), ShortcutCommand.CopyTo, out _));
        Assert.Equal(ShortcutMap.Outcome.Assigned, map.Assign(new ActionShortcut("F6"), ShortcutCommand.CopyTo, out _));
        Assert.Equal(ShortcutMap.Outcome.Reserved, map.Assign(new ActionShortcut("C", ActionModifiers.Control), ShortcutCommand.CopyTo, out _));
    }

    [Fact]
    public void ResetDoesNotTakeADefaultAnotherCommandHolds()
    {
        var map = new ShortcutMap();
        map.Assign(new ActionShortcut("F2"), ShortcutCommand.MoveTo, out var from);
        Assert.Equal(ShortcutCommand.Rename, from);
        map.Reset(ShortcutCommand.Rename);
        Assert.Null(map.Shortcut(ShortcutCommand.Rename));
        map.ResetAll();
        Assert.True(map.IsDefault);
        Assert.Equal(new ActionShortcut("F2"), map.Shortcut(ShortcutCommand.Rename));
    }

    [Fact]
    public void TheMapSurvivesJson()
    {
        var map = new ShortcutMap();
        map.Assign(new ActionShortcut("J", ActionModifiers.Control | ActionModifiers.Shift), ShortcutCommand.NewAlbum, out _);
        map.Assign(null, ShortcutCommand.MoveToTrash, out _);
        var read = ShortcutMap.FromJson(map.ToJson());
        Assert.Equal(new ActionShortcut("J", ActionModifiers.Control | ActionModifiers.Shift), read.Shortcut(ShortcutCommand.NewAlbum));
        Assert.Null(read.Shortcut(ShortcutCommand.MoveToTrash));
        Assert.True(ShortcutMap.FromJson("[").IsDefault);
    }

    [Theory]
    [InlineData("#1f3bff", "#1F3BFF")]
    [InlineData(" A396F4 ", "#A396F4")]
    public void AccentColorTextReadsAndNormalizes(string typed, string saved)
    {
        Assert.True(AccentColorText.TryParse(typed, out var argb));
        Assert.Equal(saved, AccentColorText.Format(argb));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("blue")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void AccentColorTextRefusesTheRest(string? typed) => Assert.False(AccentColorText.TryParse(typed, out _));

    [Fact]
    public void ABrokenSavedAccentFallsBackToTheDefault()
    {
        Assert.Null(AppSettings.FromJson("""{"accentColor":"nope"}""").AccentColor);
        Assert.Equal("#FF2E93", AppSettings.FromJson("""{"accentColor":"ff2e93"}""").AccentColor);
    }

    [Fact]
    public void TheRemovedFullWaveformReadsAsTheWaveform() =>
        Assert.Equal(WaveformStyle.Narrow, AppSettings.FromJson("""{"waveform":"full"}""").Waveform);
}
