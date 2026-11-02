using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace JGlancer.Platform.Windows.Setup;

/// <summary>Who the app is installed for (W-73).</summary>
public enum InstallScope
{
    /// <summary>The current user: %LOCALAPPDATA%\Programs, HKCU, no administrator.</summary>
    CurrentUser,

    /// <summary>Everyone on the PC: Program Files, HKLM, administrator.</summary>
    AllUsers,
}

/// <summary>
/// Where an installation puts things: the program folder, the Start menu and desktop folders, and the registry's
/// Software key for the Apps & features entry (opened when first needed: the machine's only with administrator
/// rights). Real for the user or the machine; tests pass temporary ones.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class InstallLayout(InstallScope scope, string programDirectory, string startMenuDirectory, string desktopDirectory, Func<RegistryKey> software)
{
    private RegistryKey? _software;

    public InstallScope Scope { get; } = scope;

    public string ProgramDirectory { get; } = programDirectory;

    public string StartMenuDirectory { get; } = startMenuDirectory;

    public string DesktopDirectory { get; } = desktopDirectory;

    /// <summary>HKCU\Software or HKLM\Software (writable), or a test key.</summary>
    public RegistryKey Software => _software ??= software();

    public const string AppName = "jGlancer";

    public const string ExecutableName = "jGlancer.exe";

    /// <summary>The Apps & features entry under the Software key.</summary>
    public const string UninstallKey = @"Microsoft\Windows\CurrentVersion\Uninstall\jGlancer";

    public string Executable => Path.Combine(ProgramDirectory, ExecutableName);

    public string StartMenuShortcut => Path.Combine(StartMenuDirectory, AppName + ".lnk");

    public string DesktopShortcut => Path.Combine(DesktopDirectory, AppName + ".lnk");

    /// <summary>The usual places for <paramref name="scope"/>; <paramref name="programDirectory"/> overrides the folder.</summary>
    public static InstallLayout For(InstallScope scope, string? programDirectory = null)
    {
        var allUsers = scope == InstallScope.AllUsers;
        var programs = allUsers
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        return new InstallLayout(
            scope,
            programDirectory ?? programs,
            Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonPrograms : Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonDesktopDirectory : Environment.SpecialFolder.DesktopDirectory),
            () => (allUsers ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey("Software", writable: true)!);
    }

    /// <summary>The installation already there, for the user first, then for everyone; null when there is none.</summary>
    public static InstalledApp? FindInstalled()
    {
        foreach (var scope in new[] { InstallScope.CurrentUser, InstallScope.AllUsers })
        {
            var root = scope == InstallScope.AllUsers ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = root.OpenSubKey(@"Software\" + UninstallKey);
            if (key?.GetValue("InstallLocation") is string location && File.Exists(Path.Combine(location, ExecutableName)))
            {
                return new InstalledApp(scope, location, key.GetValue("DisplayVersion") as string ?? "");
            }
        }
        return null;
    }
}

/// <summary>An installation found on the PC.</summary>
public sealed record InstalledApp(InstallScope Scope, string Directory, string Version);

/// <summary>
/// Installs and removes the app (W-73). Copies the program's files, writes the Start menu and optional desktop
/// shortcuts and the Apps & features entry; removing takes all of that away again, with the Explorer entries and the
/// sign-in helper, and — only when asked — the user's library, thumbnail cache and settings. Never touches media files.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Installer
{
    /// <summary>
    /// Copies every file of <paramref name="source"/> into the program folder (replacing an older version's), then the
    /// shortcuts and the entry. <paramref name="progress"/> gets the share copied so far (0…1).
    /// </summary>
    public static void Install(string source, InstallLayout layout, string version, bool desktopShortcut, Action<double>? progress = null, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(layout);
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        var total = Math.Max(1, files.Sum(f => new FileInfo(f).Length));
        long copied = 0;
        Directory.CreateDirectory(layout.ProgramDirectory);
        foreach (var file in files)
        {
            cancellation.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(layout.ProgramDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            copied += new FileInfo(file).Length;
            progress?.Invoke((double)copied / total);
        }
        ShellLink.Create(layout.StartMenuShortcut, layout.Executable, InstallTexts.Description);
        if (desktopShortcut)
        {
            ShellLink.Create(layout.DesktopShortcut, layout.Executable, InstallTexts.Description);
        }
        else if (File.Exists(layout.DesktopShortcut))
        {
            File.Delete(layout.DesktopShortcut);
        }
        WriteEntry(layout, version, total);
    }

    private static void WriteEntry(InstallLayout layout, string version, long bytes)
    {
        using var entry = layout.Software.CreateSubKey(InstallLayout.UninstallKey, writable: true);
        entry.SetValue("DisplayName", InstallLayout.AppName);
        entry.SetValue("DisplayVersion", version);
        entry.SetValue("Publisher", InstallLayout.AppName);
        entry.SetValue("DisplayIcon", layout.Executable + ",0");
        entry.SetValue("InstallLocation", layout.ProgramDirectory);
        entry.SetValue("UninstallString", $"\"{layout.Executable}\" --uninstall");
        entry.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        entry.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, bytes / 1024), RegistryValueKind.DWord);
        entry.SetValue("NoModify", 1, RegistryValueKind.DWord);
        entry.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Removes the installation: shortcuts, the entry, the program folder (best effort for files in use), and, when
    /// <paramref name="userData"/> lists them, the user's data folders. Explorer entries and the sign-in helper go too.
    /// </summary>
    public static void Uninstall(InstallLayout layout, IReadOnlyList<string> userData, RegistryKey? currentUserSoftware = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(userData);
        foreach (var shortcut in new[] { layout.StartMenuShortcut, layout.DesktopShortcut })
        {
            if (File.Exists(shortcut) && string.Equals(ShellLink.TargetOf(shortcut), layout.Executable, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(shortcut);
            }
        }
        // The Explorer entries and the Run value of this user point at the program being removed.
        var software = currentUserSoftware ?? Registry.CurrentUser.OpenSubKey("Software", writable: true)!;
        new ExplorerRegistration(software, layout.Executable) { NotifiesExplorer = currentUserSoftware is null }.Unregister();
        using (var run = software.OpenSubKey(@"Microsoft\Windows\CurrentVersion\Run", writable: true))
        {
            if (run is not null)
            {
                new LoginStart(run, layout.Executable).Disable();
            }
        }
        layout.Software.DeleteSubKeyTree(InstallLayout.UninstallKey, throwOnMissingSubKey: false);
        DeleteDirectory(layout.ProgramDirectory);
        foreach (var folder in userData)
        {
            DeleteDirectory(folder);
        }
    }

    /// <summary>Deletes a folder and what is in it; files that cannot go now (in use) go at the next restart.</summary>
    private static void DeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception fileError) when (fileError is IOException or UnauthorizedAccessException)
                {
                    Win32.DeleteAtRestart(file);
                }
            }
            Win32.DeleteAtRestart(directory);
        }
    }
}

/// <summary>Texts the installation writes into Windows (the shortcut's tooltip), set by the app in its language.</summary>
public static class InstallTexts
{
    public static string Description { get; set; } = "Fast, light media browser";
}
