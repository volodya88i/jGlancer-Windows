using JGlancer.App.Support;
using JGlancer.Core.Actions;
using JGlancer.Core.Model;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Actions;

/// <summary>An entry of an Open With submenu.</summary>
internal abstract record OpenWithEntry
{
    /// <summary>A program the user chose; <paramref name="IsInstalled"/> false when its executable is gone.</summary>
    public sealed record Configured(ExternalApp App, bool IsInstalled, bool IsPrimary) : OpenWithEntry;

    /// <summary>A program Windows recommends for the file type.</summary>
    public sealed record Suggested(ShellHandler Handler) : OpenWithEntry;

    public sealed record Separator : OpenWithEntry;

    /// <summary>Choose another program (Windows' "Open with" dialog, or an executable for several files).</summary>
    public sealed record Other : OpenWithEntry;
}

/// <summary>
/// Open With settings, saved in open-with.json (not for developer runs), and the entries of the Open With submenus
/// (macOS OpenWithStore, FEATURES §9). The settings page comes with W9 (W-46).
/// </summary>
internal sealed class OpenWithStore(JsonFile file)
{
    public OpenWithSettings Settings { get; } = OpenWithSettings.FromJson(file.Read());

    public void Update(Action<OpenWithSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        change(Settings);
        file.Write(Settings.ToJson());
    }

    /// <summary>
    /// Programs chosen for <paramref name="kind"/> (primary first), then the ones Windows recommends for
    /// <paramref name="sample"/>'s type (its default first, without the chosen ones and this app), then Other….
    /// The suggested entries hold shell objects: dispose them with <see cref="Release"/>.
    /// </summary>
    public IReadOnlyList<OpenWithEntry> Entries(MediaKind? kind, string? sample)
    {
        var entries = new List<OpenWithEntry>();
        var configured = kind is { } k ? Settings.AppsFor(k) : [];
        var primary = kind is { } p ? Settings.Primary(p) : null;
        entries.AddRange(configured.Select(app => new OpenWithEntry.Configured(app, File.Exists(app.Path), primary is not null && app.IsSame(primary))));
        var extension = sample is null ? "" : Path.GetExtension(sample);
        if (extension.Length > 1)
        {
            var self = Environment.ProcessPath ?? "";
            var suggested = new List<OpenWithEntry>();
            foreach (var handler in ShellOpenWith.Recommended(extension))
            {
                if (string.Equals(handler.Name, self, StringComparison.OrdinalIgnoreCase)
                    || configured.Any(app => string.Equals(app.Path, handler.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    handler.Dispose();
                    continue;
                }
                suggested.Add(new OpenWithEntry.Suggested(handler));
            }
            if (entries.Count > 0 && suggested.Count > 0)
            {
                entries.Add(new OpenWithEntry.Separator());
            }
            entries.AddRange(suggested);
        }
        if (entries.Count > 0)
        {
            entries.Add(new OpenWithEntry.Separator());
        }
        entries.Add(new OpenWithEntry.Other());
        return entries;
    }

    /// <summary>Lets go of the shell objects of <paramref name="entries"/>.</summary>
    public static void Release(IEnumerable<OpenWithEntry> entries)
    {
        foreach (var entry in entries.OfType<OpenWithEntry.Suggested>())
        {
            entry.Handler.Dispose();
        }
    }
}
