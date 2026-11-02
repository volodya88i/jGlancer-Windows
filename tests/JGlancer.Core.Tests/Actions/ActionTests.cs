using JGlancer.Core.Actions;
using JGlancer.Core.Model;

namespace JGlancer.Core.Tests.Actions;

/// <summary>Ports of the macOS ActionRegistryTests and OpenWithSettingsTests.</summary>
public sealed class ActionRegistryTests
{
    private static readonly HashSet<ActionFeature> W6 = [ActionFeature.OpenWith];
    private static readonly HashSet<ActionFeature> Everything = [.. Enum.GetValues<ActionFeature>()];
    private static readonly HashSet<MediaKind> NoPrimary = [];

    private static ActionSelection Selection(int count, ActionPlace place, params MediaKind[] kinds) => new(count, kinds.ToHashSet(), place);

    private static List<List<ActionId>> Ids(IReadOnlyList<IReadOnlyList<ActionMenuItem>> menu) => menu.Select(s => s.Select(i => i.Id).ToList()).ToList();

    [Fact]
    public void TheW6MenuForOnePhoto()
    {
        var menu = Ids(ActionRegistry.Menu(Selection(1, ActionPlace.Library, MediaKind.Photo), W6, NoPrimary));
        Assert.Equal(
            [[ActionId.Open, ActionId.QuickPreview, ActionId.OpenWith], [ActionId.GetInfo, ActionId.ShowInExplorer, ActionId.CopyPath, ActionId.Copy]],
            menu);
    }

    [Fact]
    public void CompareTakesExactlyTwoPhotosAndTheCompareViewOnlyReads()
    {
        Assert.Equal(ActionAvailability.Enabled, ActionRegistry.Availability(ActionId.Compare, Selection(2, ActionPlace.Library, MediaKind.Photo), Everything, NoPrimary));
        Assert.Equal(ActionAvailability.Enabled, ActionRegistry.Availability(ActionId.Compare, Selection(2, ActionPlace.Album, MediaKind.Photo), Everything, NoPrimary));
        Assert.Equal(ActionAvailability.Hidden, ActionRegistry.Availability(ActionId.Compare, Selection(3, ActionPlace.Library, MediaKind.Photo), Everything, NoPrimary));
        Assert.Equal(ActionAvailability.Hidden, ActionRegistry.Availability(ActionId.Compare, Selection(2, ActionPlace.Library, MediaKind.Photo, MediaKind.Video), Everything, NoPrimary));
        var compared = Selection(2, ActionPlace.Compare, MediaKind.Photo);
        Assert.Equal(
            [[ActionId.GetInfo, ActionId.ShowInExplorer, ActionId.CopyPath, ActionId.Copy]],
            Ids(ActionRegistry.Menu(compared, Everything, NoPrimary)));
        Assert.Equal(ActionAvailability.Hidden, ActionRegistry.Availability(ActionId.Flag, compared, Everything, NoPrimary));
    }

    [Fact]
    public void EditInPrimaryAppNeedsAPrimaryForTheOnlyKind()
    {
        var audio = Selection(2, ActionPlace.Library, MediaKind.Audio);
        var mixed = Selection(2, ActionPlace.Library, MediaKind.Audio, MediaKind.Photo);
        Assert.Equal(ActionAvailability.Enabled, ActionRegistry.Availability(ActionId.EditInPrimaryApp, audio, W6, new HashSet<MediaKind> { MediaKind.Audio }));
        Assert.Equal(ActionAvailability.Hidden, ActionRegistry.Availability(ActionId.EditInPrimaryApp, audio, W6, new HashSet<MediaKind> { MediaKind.Photo }));
        Assert.Equal(ActionAvailability.Hidden, ActionRegistry.Availability(ActionId.EditInPrimaryApp, mixed, W6, new HashSet<MediaKind> { MediaKind.Audio, MediaKind.Photo }));
    }

    [Fact]
    public void RotateAndEditArePhotosOnly()
    {
        var photos = Selection(3, ActionPlace.Library, MediaKind.Photo);
        var video = Selection(1, ActionPlace.Library, MediaKind.Video);
        Assert.Equal(ActionAvailability.Enabled, ActionRegistry.Availability(ActionId.RotateLeft, photos, Everything, NoPrimary));
        Assert.Equal(ActionAvailability.Hidden, ActionRegistry.Availability(ActionId.RotateLeft, video, Everything, NoPrimary));
        Assert.Equal(ActionAvailability.Disabled, ActionRegistry.Availability(ActionId.Edit, photos, Everything, NoPrimary));
        Assert.Equal(ActionAvailability.Enabled, ActionRegistry.Availability(ActionId.Rename, photos, Everything, NoPrimary));
    }

    [Fact]
    public void TheFullMenuEndsWithTrashAloneAndAViewerHasNoQuickPreview()
    {
        var menu = Ids(ActionRegistry.Menu(Selection(1, ActionPlace.Viewer, MediaKind.Photo), Everything, new HashSet<MediaKind> { MediaKind.Photo }));
        Assert.Equal([ActionId.MoveToTrash], menu[^1]);
        var all = menu.SelectMany(s => s).ToList();
        Assert.DoesNotContain(ActionId.QuickPreview, all);
        Assert.Contains(ActionId.CopyTo, all);
        Assert.DoesNotContain(ActionId.MoveTo, all);
        Assert.DoesNotContain(ActionId.Rename, all);
        Assert.Equal([ActionId.OpenWith, ActionId.EditInPrimaryApp], menu[0]);
        Assert.Contains(menu, s => s.SequenceEqual([ActionId.Flag]));
    }

    [Fact]
    public void NothingForAnEmptySelection() =>
        Assert.Empty(ActionRegistry.Menu(Selection(0, ActionPlace.Library), Everything, NoPrimary));

    [Fact]
    public void PlainKeysStayOutOfTheMenuBar()
    {
        Assert.False(ActionRegistry.Shortcut(ActionId.QuickPreview)!.IsMenuBarEquivalent);
        Assert.False(ActionRegistry.Shortcut(ActionId.Open)!.IsMenuBarEquivalent);
        Assert.True(ActionRegistry.Shortcut(ActionId.GetInfo)!.IsMenuBarEquivalent);
        Assert.Equal("Ctrl+Alt+E", ActionRegistry.Shortcut(ActionId.EditInPrimaryApp)!.Gesture);
        var all = ActionRegistry.Sections.SelectMany(s => s).ToList();
        Assert.Equal(Enum.GetValues<ActionId>().Length, all.Distinct().Count());
    }

    [Fact]
    public void AlbumActionsAppearInAlbums()
    {
        var inAlbum = Selection(2, ActionPlace.Album, MediaKind.Photo);
        var inLibrary = Selection(1, ActionPlace.Library, MediaKind.Photo);
        Assert.Equal(ActionAvailability.Enabled, ActionRegistry.Availability(ActionId.RemoveFromAlbum, inAlbum, Everything, NoPrimary));
        Assert.Equal(ActionAvailability.Disabled, ActionRegistry.Availability(ActionId.SetAlbumCover, inAlbum, Everything, NoPrimary));
        Assert.Equal(ActionAvailability.Hidden, ActionRegistry.Availability(ActionId.RemoveFromAlbum, inLibrary, Everything, NoPrimary));
        Assert.Equal(ActionAvailability.Enabled, ActionRegistry.Availability(ActionId.QuickPreview, inAlbum, Everything, NoPrimary));
    }
}

public sealed class OpenWithSettingsTests
{
    private static readonly ExternalApp Audition = new(@"C:\Program Files\Adobe\Audition.exe", "Audition");
    private static readonly ExternalApp Rx = new(@"C:\Program Files\iZotope\RX.exe", "RX");

    [Fact]
    public void TheFirstAppBecomesPrimaryAndRemovingItPromotesTheNext()
    {
        var settings = new OpenWithSettings();
        settings.Add(Audition, MediaKind.Audio);
        settings.Add(Rx, MediaKind.Audio);
        Assert.Equal(Audition, settings.Primary(MediaKind.Audio));
        settings.SetPrimary(Rx.Path.ToUpperInvariant(), MediaKind.Audio);
        Assert.Equal([Rx, Audition], settings.AppsFor(MediaKind.Audio));
        settings.Remove(Rx.Path, MediaKind.Audio);
        Assert.Equal(Audition, settings.Primary(MediaKind.Audio));
        Assert.Equal(new HashSet<MediaKind> { MediaKind.Audio }, settings.KindsWithPrimary);
    }

    [Fact]
    public void AddingTheSameProgramUpdatesItInPlace()
    {
        var settings = new OpenWithSettings();
        settings.Add(Audition, MediaKind.Audio);
        settings.Add(Audition with { Path = Audition.Path.ToLowerInvariant(), Name = "Adobe Audition" }, MediaKind.Audio);
        Assert.Single(settings.AppsFor(MediaKind.Audio));
        Assert.Equal("Adobe Audition", settings.AppsFor(MediaKind.Audio)[0].Name);
    }

    [Fact]
    public void SettingsSurviveJsonAndBadJsonGivesNone()
    {
        var settings = new OpenWithSettings();
        settings.Add(Rx, MediaKind.Audio);
        settings.Add(Audition, MediaKind.Video);
        var read = OpenWithSettings.FromJson(settings.ToJson());
        Assert.Equal(Rx, read.Primary(MediaKind.Audio));
        Assert.Equal(Audition, read.Primary(MediaKind.Video));
        Assert.Empty(OpenWithSettings.FromJson("{ nope").KindsWithPrimary);
    }
}
