using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Core.Model;
using JGlancer.Core.Scanning;
using Microsoft.Win32;

namespace JGlancer.Platform.Windows;

/// <summary>
/// The app in Explorer for the current user only (HKCU, no administrator; W11, WINDOWS_PORT): an Open With choice for
/// every photo, video and audio type it opens (<c>OpenWithProgids</c>, never the default — like macOS
/// LSHandlerRank Alternate), a registered application for Windows' Default Apps (where the user may make it the
/// default), "Quick Preview in jGlancer" on media files and "Open in jGlancer" on folders. <see cref="Unregister"/>
/// removes all of it. Every key lives under the given software root (HKCU\Software normally; tests use a key of their
/// own).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ExplorerRegistration(RegistryKey software, string executable)
{
    public const string ApplicationName = "jGlancer";
    public const string QuickPreviewVerb = "jGlancer.QuickPreview";
    public const string OpenFolderVerb = "jGlancer.Open";
    private const string ExecutableName = "jGlancer.exe";
    // The capabilities under the Software key this registration writes to (HKCU\Software), and the same key as
    // RegisteredApplications names it, from the hive's root. Until 0.3.4 the first was written one "Software" too deep
    // (HKCU\Software\Software\jGlancer), so Windows' Default Apps never listed jGlancer (W-80).
    private const string CapabilitiesPath = @"jGlancer\Capabilities";
    private const string RegisteredCapabilitiesPath = @"Software\jGlancer\Capabilities";
    private const string MisplacedRoot = "Software";

    /// <summary>Tell Explorer to reload its menus and Open With lists (off in tests, which write to a key of their own).</summary>
    public bool NotifiesExplorer { get; init; } = true;

    /// <summary>The texts Explorer shows, in the app's language.</summary>
    public sealed record Texts(string QuickPreview, string OpenFolder, string Description, string Photo, string Video, string Audio);

    /// <summary>The current user's registry: HKCU\Software.</summary>
    public static ExplorerRegistration ForCurrentUser(string executable) =>
        new(Registry.CurrentUser.OpenSubKey("Software", writable: true)!, executable);

    private string OpenCommand => $"\"{executable}\" \"%1\"";

    public static string ProgId(MediaKind kind) => "jGlancer." + kind;

    /// <summary>True when Explorer knows this executable (an older registration elsewhere does not count).</summary>
    public bool IsRegistered
    {
        get
        {
            using var command = software.OpenSubKey($@"Classes\Applications\{ExecutableName}\shell\open\command");
            return command?.GetValue(null) as string == OpenCommand;
        }
    }

    public void Register(Texts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var icon = $"\"{executable}\",0";
        using var classes = software.CreateSubKey("Classes");
        foreach (var kind in new[] { MediaKind.Photo, MediaKind.Video, MediaKind.Audio })
        {
            using var progId = classes.CreateSubKey(ProgId(kind));
            progId.SetValue(null, kind switch { MediaKind.Photo => texts.Photo, MediaKind.Video => texts.Video, _ => texts.Audio });
            using (var defaultIcon = progId.CreateSubKey("DefaultIcon"))
            {
                defaultIcon.SetValue(null, icon);
            }
            SetCommand(progId, "open", OpenCommand);
        }
        using (var application = classes.CreateSubKey($@"Applications\{ExecutableName}"))
        {
            application.SetValue("FriendlyAppName", ApplicationName);
            SetCommand(application, "open", OpenCommand);
            using var supported = application.CreateSubKey("SupportedTypes");
            foreach (var (extension, _) in MediaClassifier.All)
            {
                supported.SetValue("." + extension, "");
            }
        }
        using (var capabilities = software.CreateSubKey(CapabilitiesPath))
        {
            capabilities.SetValue("ApplicationName", ApplicationName);
            capabilities.SetValue("ApplicationDescription", texts.Description);
            capabilities.SetValue("ApplicationIcon", icon);
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var (extension, type) in MediaClassifier.All)
            {
                associations.SetValue("." + extension, ProgId(type.Kind));
            }
        }
        RemoveMisplaced();
        using (var registered = software.CreateSubKey("RegisteredApplications"))
        {
            registered.SetValue(ApplicationName, RegisteredCapabilitiesPath);
        }
        foreach (var (extension, type) in MediaClassifier.All)
        {
            using (var openWith = classes.CreateSubKey($@".{extension}\OpenWithProgids"))
            {
                openWith.SetValue(ProgId(type.Kind), Array.Empty<byte>(), RegistryValueKind.None);
            }
            using var verb = classes.CreateSubKey($@"SystemFileAssociations\.{extension}\shell\{QuickPreviewVerb}");
            verb.SetValue("MUIVerb", texts.QuickPreview);
            verb.SetValue("Icon", icon);
            SetCommand(verb, null, $"\"{executable}\" --preview \"%1\"");
        }
        foreach (var place in new[] { @"Directory\shell", @"Directory\Background\shell" })
        {
            using var verb = classes.CreateSubKey($@"{place}\{OpenFolderVerb}");
            verb.SetValue("MUIVerb", texts.OpenFolder);
            verb.SetValue("Icon", icon);
            SetCommand(verb, null, $"\"{executable}\" \"%V\"");
        }
        NotifyExplorer();
    }

    /// <summary>
    /// A registration made before 0.3.4 (capabilities in the wrong place, so Default Apps never listed jGlancer) is
    /// written again; anything else is left as it is. True when it repaired one.
    /// </summary>
    public bool RepairIfMisplaced(Texts texts)
    {
        using (var misplaced = software.OpenSubKey($@"{MisplacedRoot}\jGlancer"))
        {
            if (misplaced is null || !IsRegistered)
            {
                return false;
            }
        }
        Register(texts);
        return true;
    }

    /// <summary>The capabilities older versions wrote one "Software" too deep; the empty "Software" key goes too.</summary>
    private void RemoveMisplaced()
    {
        using (var misplaced = software.OpenSubKey(MisplacedRoot, writable: true))
        {
            if (misplaced is null)
            {
                return;
            }
            misplaced.DeleteSubKeyTree("jGlancer", throwOnMissingSubKey: false);
            if (misplaced.SubKeyCount > 0 || misplaced.ValueCount > 0)
            {
                return;
            }
        }
        software.DeleteSubKey(MisplacedRoot, throwOnMissingSubKey: false);
    }

    /// <summary>Removes every key and value <see cref="Register"/> wrote (other programs' entries stay).</summary>
    public void Unregister()
    {
        using (var classes = software.OpenSubKey("Classes", writable: true))
        {
            if (classes is not null)
            {
                foreach (var kind in new[] { MediaKind.Photo, MediaKind.Video, MediaKind.Audio })
                {
                    classes.DeleteSubKeyTree(ProgId(kind), throwOnMissingSubKey: false);
                }
                classes.DeleteSubKeyTree($@"Applications\{ExecutableName}", throwOnMissingSubKey: false);
                foreach (var (extension, type) in MediaClassifier.All)
                {
                    using (var openWith = classes.OpenSubKey($@".{extension}\OpenWithProgids", writable: true))
                    {
                        openWith?.DeleteValue(ProgId(type.Kind), throwOnMissingValue: false);
                    }
                    classes.DeleteSubKeyTree($@"SystemFileAssociations\.{extension}\shell\{QuickPreviewVerb}", throwOnMissingSubKey: false);
                }
                foreach (var place in new[] { @"Directory\shell", @"Directory\Background\shell" })
                {
                    classes.DeleteSubKeyTree($@"{place}\{OpenFolderVerb}", throwOnMissingSubKey: false);
                }
            }
        }
        software.DeleteSubKeyTree(CapabilitiesPath, throwOnMissingSubKey: false);
        RemoveMisplaced();
        using (var registered = software.OpenSubKey("RegisteredApplications", writable: true))
        {
            registered?.DeleteValue(ApplicationName, throwOnMissingValue: false);
        }
        NotifyExplorer();
    }

    /// <summary>
    /// Windows lets no program take a file type by itself: on the Default Apps page the user presses "Set default",
    /// which takes the types the app offers (W-80). This offers only the types of <paramref name="kinds"/> (all of them
    /// when empty), so that button makes jGlancer the default exactly for those. Registers first when needed.
    /// </summary>
    public void OfferAsDefault(IReadOnlySet<MediaKind> kinds, Texts texts)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        // Always afresh: an older registration may have its capabilities in the wrong place (before 0.3.4).
        Register(texts);
        using (var capabilities = software.CreateSubKey(CapabilitiesPath))
        {
            capabilities.DeleteSubKeyTree("FileAssociations", throwOnMissingSubKey: false);
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var (extension, type) in MediaClassifier.All)
            {
                if (kinds.Count == 0 || kinds.Contains(type.Kind))
                {
                    associations.SetValue("." + extension, ProgId(type.Kind));
                }
            }
        }
        NotifyExplorer();
    }

    /// <summary>
    /// Whether this user opens <paramref name="kind"/> with jGlancer by default, judged by its most common type (.jpg,
    /// .mp4, .mp3). Only reads Windows' choice. <paramref name="currentUserSoftware"/> is for tests.
    /// </summary>
    public static bool IsDefault(MediaKind kind, RegistryKey? currentUserSoftware = null)
    {
        var extension = kind switch { MediaKind.Photo => ".jpg", MediaKind.Video => ".mp4", _ => ".mp3" };
        var software = currentUserSoftware ?? Registry.CurrentUser.OpenSubKey("Software")!;
        var path = $@"Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}";
        // Newer Windows 11 keeps the choice in UserChoiceLatest, older builds in UserChoice.
        foreach (var choice in new[] { @"UserChoiceLatest\ProgId", "UserChoiceLatest", "UserChoice" })
        {
            using var key = software.OpenSubKey($@"{path}\{choice}");
            if (key?.GetValue("ProgId") is string progId && progId.Length > 0)
            {
                return progId == ProgId(kind) || progId.EndsWith(ExecutableName, StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    /// <summary>Windows' Default Apps page for this app (where the user may make it the default for types).</summary>
    public static Uri DefaultAppsPage { get; } = new($"ms-settings:defaultapps?registeredAppUser={ApplicationName}");

    private static void SetCommand(RegistryKey key, string? verb, string command)
    {
        using var commandKey = key.CreateSubKey(verb is null ? "command" : $@"shell\{verb}\command");
        commandKey.SetValue(null, command);
    }

    private const int AssociationsChanged = 0x08000000;
    private const uint IdList = 0;

    /// <summary>Explorer reloads its menus and Open With lists.</summary>
    private void NotifyExplorer()
    {
        if (NotifiesExplorer)
        {
            SHChangeNotify(AssociationsChanged, IdList, 0, 0);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
