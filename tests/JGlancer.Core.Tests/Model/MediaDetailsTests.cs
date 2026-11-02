using JGlancer.Core.Actions;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;

namespace JGlancer.Core.Tests.Model;

/// <summary>Port of the macOS MediaDetailsTests (names) and MediaDetailsRows; reading files is tested in Platform.</summary>
[Collection("Localization")]
public sealed class MediaDetailsTests
{
    [Fact]
    public void CameraNamesDoNotRepeatTheMaker()
    {
        Assert.Equal("Canon EOS R5", MediaDetails.CameraName("Canon", "Canon EOS R5"));
        Assert.Equal("NIKON Z 6", MediaDetails.CameraName("NIKON CORPORATION", "NIKON Z 6"));
        Assert.Equal("Apple iPhone 15 Pro", MediaDetails.CameraName("Apple", "iPhone 15 Pro"));
        Assert.Equal("Apple", MediaDetails.CameraName("Apple", null));
        Assert.Null(MediaDetails.CameraName(null, " "));
    }

    [Fact]
    public void CodecsGetTheirCommonNames()
    {
        Assert.Equal("H.264", MediaDetails.CodecName("avc1"));
        Assert.Equal("HEVC", MediaDetails.CodecName("HEVC"));
        Assert.Equal("PCM", MediaDetails.CodecName("pcm_s24le"));
        Assert.Equal("xyz9", MediaDetails.CodecName("xyz9"));
    }

    [Fact]
    public void RowsUseTheUnitsOfTheLanguage()
    {
        var details = new MediaDetails
        {
            Camera = "Apple iPhone 15 Pro", FocalLength = 6.86, Aperture = 1.8, ExposureTime = 0.008, Iso = 125,
            Latitude = 55.75, Longitude = 37.62, BitDepth = 24,
        };
        var previous = (L.Language, System.Globalization.CultureInfo.CurrentCulture);
        try
        {
            L.Use("en");
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var rows = MediaDetailsRows.Rows(details).ToDictionary(r => r.Label, r => r.Value);
            Assert.Equal("6.9 mm", rows["Focal Length"]);
            Assert.Equal("ƒ/1.8", rows["Aperture"]);
            Assert.Equal("1/125 s", rows["Exposure"]);
            Assert.Equal("55.75000, 37.62000", rows["Location"]);
            Assert.Equal("24 bit", rows["Bit Depth"]);

            L.Use("ru");
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            var audio = MediaDetailsRows.Rows(new MediaDetails { AudioCodec = "AAC", AudioBitrate = 256_000, SampleRate = 44_100, Channels = 2, BitDepth = 16 })
                .ToDictionary(r => r.Label, r => r.Value);
            Assert.Equal("256 кбит/с", audio["Битрейт аудио"]);
            Assert.Equal("44,1 кГц", audio["Частота дискретизации"]);
            Assert.Equal("Стерео", audio["Каналы"]);
            Assert.Equal("16 бит", audio["Разрядность"]);
        }
        finally
        {
            L.Use(previous.Item1);
            System.Globalization.CultureInfo.CurrentCulture = previous.Item2;
        }
    }

    [Fact]
    public void FlagCommandsHaveTheChordKeyAndNoColorDefaults()
    {
        Assert.Equal(new ActionShortcut("F"), ShortcutCommand.FlagAccent.Default());
        Assert.Null(ShortcutCommand.FlagRed.Default());
        Assert.Equal(new ActionShortcut("F", ActionModifiers.Control), ShortcutCommand.Find.Default());
        Assert.Equal(ShortcutCommand.FlagBlue, ShortcutCommands.CommandFor(AssetFlag.Blue));
        Assert.Equal(8, ShortcutCommands.Flags.Count);
        Assert.Contains(ShortcutCommand.Find, ShortcutCommands.Library);
        Assert.DoesNotContain(ShortcutCommand.FlagAccent, ShortcutCommands.Library);
    }

    [Fact]
    public void TheFlagFilterIsSaved()
    {
        var state = new LibraryViewState { FlagFilter = new FlagFilter(AssetFlag.Green) };
        Assert.Equal(AssetFlag.Green, LibraryViewState.FromJson(state.ToJson()).FlagFilter?.Only);
        Assert.Null(LibraryViewState.FromJson(new LibraryViewState { FlagFilter = FlagFilter.Flagged }.ToJson()).FlagFilter?.Only);
        Assert.NotNull(LibraryViewState.FromJson(new LibraryViewState { FlagFilter = FlagFilter.Flagged }.ToJson()).FlagFilter);
    }
}
