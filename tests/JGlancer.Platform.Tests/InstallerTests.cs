using JGlancer.Core.Tests.Support;
using JGlancer.Platform.Windows;
using JGlancer.Platform.Windows.Setup;
using Microsoft.Win32;

namespace JGlancer.Platform.Tests;

/// <summary>Install, upgrade and uninstall into temporary folders and a temporary registry key (never the PC's own).</summary>
public sealed class InstallerTests
{
    [Fact]
    public void InstallingUpgradingAndUninstallingLeaveNothingBehind()
    {
        using var folder = new TemporaryDirectory();
        folder.Write(@"source\jGlancer.exe", "program");
        folder.Write(@"source\libmpv-2.dll", "player");
        folder.Write(@"source\Licenses\THIRD-PARTY-NOTICES.txt", "notices");
        var data = folder.Write(@"data\library.sqlite", "library");
        var name = $@"Software\jGlancer-Tests-{Guid.NewGuid():N}";
        using var software = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            var layout = new InstallLayout(InstallScope.CurrentUser, folder.Full("Programs\\jGlancer"), folder.Full("Start"), folder.Full("Desktop"), () => software);
            var reported = new List<double>();

            Installer.Install(folder.Full("source"), layout, "0.2.0", desktopShortcut: true, reported.Add);

            Assert.Equal("program", File.ReadAllText(layout.Executable));
            Assert.True(File.Exists(Path.Combine(layout.ProgramDirectory, "Licenses", "THIRD-PARTY-NOTICES.txt")));
            Assert.Equal(1, reported[^1], precision: 6);
            Assert.Equal(layout.Executable, ShellLink.TargetOf(layout.StartMenuShortcut), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(layout.Executable, ShellLink.TargetOf(layout.DesktopShortcut), StringComparer.OrdinalIgnoreCase);
            using (var entry = software.OpenSubKey(InstallLayout.UninstallKey)!)
            {
                Assert.Equal("0.2.0", entry.GetValue("DisplayVersion"));
                Assert.Equal(layout.ProgramDirectory, entry.GetValue("InstallLocation"));
                Assert.Equal($"\"{layout.Executable}\" --uninstall", entry.GetValue("UninstallString"));
            }

            // An update over it: new files, the newer version, the desktop shortcut no longer wanted.
            folder.Write(@"source\jGlancer.exe", "program 2");
            Installer.Install(folder.Full("source"), layout, "0.3.0", desktopShortcut: false);
            Assert.Equal("program 2", File.ReadAllText(layout.Executable));
            Assert.False(File.Exists(layout.DesktopShortcut));
            using (var entry = software.OpenSubKey(InstallLayout.UninstallKey)!)
            {
                Assert.Equal("0.3.0", entry.GetValue("DisplayVersion"));
            }

            // The user had registered with Explorer and turned the sign-in helper on.
            new ExplorerRegistration(software, layout.Executable) { NotifiesExplorer = false }
                .Register(new("Quick Preview in jGlancer", "Open in jGlancer", "Media viewer", "Photo", "Video", "Audio"));
            using (var run = software.CreateSubKey(@"Microsoft\Windows\CurrentVersion\Run"))
            {
                new LoginStart(run, layout.Executable).Enable();
            }

            Installer.Uninstall(layout, [Path.GetDirectoryName(data)!], currentUserSoftware: software);

            Assert.False(Directory.Exists(layout.ProgramDirectory));
            Assert.False(File.Exists(layout.StartMenuShortcut));
            Assert.False(Directory.Exists(Path.GetDirectoryName(data)));
            Assert.Null(software.OpenSubKey(InstallLayout.UninstallKey));
            Assert.False(new ExplorerRegistration(software, layout.Executable) { NotifiesExplorer = false }.IsRegistered);
            using (var run = software.OpenSubKey(@"Microsoft\Windows\CurrentVersion\Run")!)
            {
                Assert.Empty(run.GetValueNames());
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void ShortcutsOfAnotherProgramAreLeftAlone()
    {
        using var folder = new TemporaryDirectory();
        folder.Write(@"other\Other.exe", "other");
        var name = $@"Software\jGlancer-Tests-{Guid.NewGuid():N}";
        using var software = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            var layout = new InstallLayout(InstallScope.CurrentUser, folder.Full("Programs\\jGlancer"), folder.Full("Start"), folder.Full("Desktop"), () => software);
            // A "jGlancer.lnk" the user made for something else is not ours to delete.
            ShellLink.Create(layout.StartMenuShortcut, folder.Full(@"other\Other.exe"), "Other");
            Installer.Uninstall(layout, [], currentUserSoftware: software);
            Assert.True(File.Exists(layout.StartMenuShortcut));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }
}
