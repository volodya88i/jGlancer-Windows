using JGlancer.Core.Model;
using JGlancer.Platform.Windows;
using Microsoft.Win32;

namespace JGlancer.Platform.Tests;

/// <summary>Registration written under a temporary key of its own (never the user's Explorer settings), then removed.</summary>
public sealed class ExplorerRegistrationTests
{
    private static readonly ExplorerRegistration.Texts Texts = new("Quick Preview in jGlancer", "Open in jGlancer", "Media viewer", "Photo", "Video", "Audio");

    [Fact]
    public void RegisteringAddsOpenWithMenusAndCapabilitiesAndUnregisteringRemovesThem()
    {
        var name = $@"Software\jGlancer-Tests-{Guid.NewGuid():N}";
        using var root = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            var registration = new ExplorerRegistration(root, @"C:\Apps\jGlancer.exe") { NotifiesExplorer = false };
            Assert.False(registration.IsRegistered);
            registration.Register(Texts);
            Assert.True(registration.IsRegistered);
            Assert.False(new ExplorerRegistration(root, @"D:\Other\jGlancer.exe") { NotifiesExplorer = false }.IsRegistered);

            using (var openWith = root.OpenSubKey(@"Classes\.jpg\OpenWithProgids"))
            {
                Assert.Contains(ExplorerRegistration.ProgId(MediaKind.Photo), openWith!.GetValueNames());
            }
            using (var command = root.OpenSubKey(@"Classes\jGlancer.Video\shell\open\command"))
            {
                Assert.Equal(@"""C:\Apps\jGlancer.exe"" ""%1""", command!.GetValue(null));
            }
            using (var preview = root.OpenSubKey($@"Classes\SystemFileAssociations\.mkv\shell\{ExplorerRegistration.QuickPreviewVerb}"))
            {
                Assert.Equal("Quick Preview in jGlancer", preview!.GetValue("MUIVerb"));
                using var command = preview.OpenSubKey("command");
                Assert.Equal(@"""C:\Apps\jGlancer.exe"" --preview ""%1""", command!.GetValue(null));
            }
            using (var folder = root.OpenSubKey($@"Classes\Directory\shell\{ExplorerRegistration.OpenFolderVerb}\command"))
            {
                Assert.Equal(@"""C:\Apps\jGlancer.exe"" ""%V""", folder!.GetValue(null));
            }
            using (var registered = root.OpenSubKey("RegisteredApplications"))
            {
                Assert.Equal(@"Software\jGlancer\Capabilities", registered!.GetValue(ExplorerRegistration.ApplicationName));
            }
            using (var associations = root.OpenSubKey(@"jGlancer\Capabilities\FileAssociations"))
            {
                Assert.Equal("jGlancer.Audio", associations!.GetValue(".flac"));
            }

            // Another program's Open With entry for the same type stays.
            using (var openWith = root.CreateSubKey(@"Classes\.jpg\OpenWithProgids"))
            {
                openWith.SetValue("Other.Viewer", "");
            }
            registration.Unregister();
            Assert.False(registration.IsRegistered);
            using (var openWith = root.OpenSubKey(@"Classes\.jpg\OpenWithProgids"))
            {
                Assert.Equal(["Other.Viewer"], openWith!.GetValueNames());
            }
            Assert.Null(root.OpenSubKey($@"Classes\SystemFileAssociations\.mkv\shell\{ExplorerRegistration.QuickPreviewVerb}"));
            Assert.Null(root.OpenSubKey(@"Classes\jGlancer.Photo"));
            Assert.Null(root.OpenSubKey(@"jGlancer\Capabilities"));
            Assert.Empty(root.OpenSubKey("RegisteredApplications")!.GetValueNames());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void TheHelperStartsAtSignInOnlyWhileEnabled()
    {
        var name = $@"Software\jGlancer-Tests-{Guid.NewGuid():N}";
        using var run = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            var start = new LoginStart(run, @"C:\Apps\jGlancer.exe");
            Assert.False(start.IsEnabled);
            start.Enable();
            Assert.True(start.IsEnabled);
            Assert.Equal(@"""C:\Apps\jGlancer.exe"" --background", run.GetValue("jGlancer"));
            Assert.False(new LoginStart(run, @"D:\Other\jGlancer.exe").IsEnabled);
            start.Disable();
            Assert.False(start.IsEnabled);
            Assert.Empty(run.GetValueNames());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void OfferingAsDefaultListsOnlyTheChosenKinds()
    {
        var name = $@"Software\jGlancer-Tests-{Guid.NewGuid():N}";
        using var root = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            var registration = new ExplorerRegistration(root, @"C:\Apps\jGlancer.exe") { NotifiesExplorer = false };
            registration.OfferAsDefault(new HashSet<MediaKind> { MediaKind.Photo }, Texts);
            Assert.True(registration.IsRegistered);
            using (var associations = root.OpenSubKey(@"jGlancer\Capabilities\FileAssociations")!)
            {
                Assert.Equal("jGlancer.Photo", associations.GetValue(".jpg"));
                Assert.Null(associations.GetValue(".mp4"));
            }
            registration.OfferAsDefault(new HashSet<MediaKind>(), Texts);
            using (var associations = root.OpenSubKey(@"jGlancer\Capabilities\FileAssociations")!)
            {
                Assert.Equal("jGlancer.Video", associations.GetValue(".mp4"));
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void TheDefaultIsReadFromWindowsChoiceNewestFirst()
    {
        var name = $@"Software\jGlancer-Tests-{Guid.NewGuid():N}";
        using var root = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            const string Exts = @"Microsoft\Windows\CurrentVersion\Explorer\FileExts\";
            Assert.False(ExplorerRegistration.IsDefault(MediaKind.Photo, root));
            using (var jpg = root.CreateSubKey(Exts + @".jpg\UserChoice"))
            {
                jpg.SetValue("ProgId", "jGlancer.Photo");
            }
            Assert.True(ExplorerRegistration.IsDefault(MediaKind.Photo, root));
            // A newer choice for another app wins over the older record.
            using (var latest = root.CreateSubKey(Exts + @".jpg\UserChoiceLatest\ProgId"))
            {
                latest.SetValue("ProgId", "AppX12345");
            }
            Assert.False(ExplorerRegistration.IsDefault(MediaKind.Photo, root));
            using (var mp3 = root.CreateSubKey(Exts + @".mp3\UserChoiceLatest\ProgId"))
            {
                mp3.SetValue("ProgId", @"Applications\jGlancer.exe");
            }
            Assert.True(ExplorerRegistration.IsDefault(MediaKind.Audio, root));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void RegisteringMovesCapabilitiesOutOfTheOldMisplacedKey()
    {
        var name = $@"Software\jGlancer-Tests-{Guid.NewGuid():N}";
        using var root = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            // Versions before 0.3.4 wrote them one "Software" too deep.
            using (var old = root.CreateSubKey(@"Software\jGlancer\Capabilities"))
            {
                old.SetValue("ApplicationName", "jGlancer");
            }
            var registration = new ExplorerRegistration(root, @"C:\Apps\jGlancer.exe") { NotifiesExplorer = false };
            registration.Register(Texts);
            Assert.Null(root.OpenSubKey("Software"));
            using (var capabilities = root.OpenSubKey(@"jGlancer\Capabilities")!)
            {
                Assert.Equal("jGlancer", capabilities.GetValue("ApplicationName"));
            }
            using (var registered = root.OpenSubKey("RegisteredApplications")!)
            {
                // Named from the hive's root, where Windows looks it up.
                Assert.Equal(@"Software\jGlancer\Capabilities", registered.GetValue("jGlancer"));
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }
}
