using System.Diagnostics;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Settings;

/// <summary>
/// jGlancer as the default app for photos, video or audio (W-80). Windows lets no program take a file type itself:
/// the app offers just the chosen kinds (and those it already opens) and opens its Default Apps page, where one press
/// of "Set default" does it. Used by Settings → Windows and by the installer.
/// </summary>
internal static class DefaultApps
{
    public static IReadOnlyList<MediaKind> Kinds { get; } = [MediaKind.Photo, MediaKind.Video, MediaKind.Audio];

    /// <summary>The texts Explorer shows, in the app's language.</summary>
    public static ExplorerRegistration.Texts Texts => new(
        L.T("Quick Preview in jGlancer"), L.T("Open in jGlancer"), L.T("Fast viewer and player for photos, video and audio"),
        L.T("Photo"), L.T("Video"), L.T("Audio"));

    public static bool IsDefault(MediaKind kind) => ExplorerRegistration.IsDefault(kind);

    /// <summary>Offers <paramref name="kinds"/> (with the kinds jGlancer already opens) and opens the Default Apps page.</summary>
    public static void Offer(string executable, IEnumerable<MediaKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var offered = Kinds.Where(IsDefault).Concat(kinds).ToHashSet();
        ExplorerRegistration.ForCurrentUser(executable).OfferAsDefault(offered, Texts);
        OpenPage();
    }

    /// <summary>The Default Apps page for jGlancer (Settings runs as the user even when asked from an administrator's setup).</summary>
    public static void OpenPage() =>
        Process.Start(new ProcessStartInfo(ExplorerRegistration.DefaultAppsPage.ToString()) { UseShellExecute = true })?.Dispose();

    /// <summary>"Photos", "Video", "Music" for the rows.</summary>
    public static string Title(MediaKind kind) => kind switch
    {
        MediaKind.Photo => L.T("Photos|default app"),
        MediaKind.Video => L.T("Video|default app"),
        _ => L.T("Music|default app"),
    };
}
