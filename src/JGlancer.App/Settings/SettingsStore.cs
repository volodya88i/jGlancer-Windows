using JGlancer.App.Support;
using JGlancer.Core.Actions;
using JGlancer.Core.Settings;

namespace JGlancer.App.Settings;

/// <summary>
/// The app's settings (macOS SettingsStore, FEATURES §14) and the user's shortcuts, saved in settings.json and
/// shortcuts.json next to the library (not in developer runs) and applied as they change; views read
/// <see cref="Current"/> and follow <see cref="Changed"/>. UI thread only.
/// </summary>
internal sealed class SettingsStore
{
    private readonly JsonFile _settingsFile;
    private readonly JsonFile _shortcutsFile;

    /// <param name="showsOnboarding">Developer runs (not persistent) skip the first-launch cards unless asked for them.</param>
    public SettingsStore(JsonFile settings, JsonFile shortcuts, bool isPersistent, bool showsOnboarding = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(shortcuts);
        _settingsFile = settings;
        _shortcutsFile = shortcuts;
        Settings = AppSettings.FromJson(settings.Read());
        if (!isPersistent)
        {
            Settings = Settings with { HasChosenAppearance = !showsOnboarding };
        }
        Shortcuts = ShortcutMap.FromJson(shortcuts.Read());
    }

    /// <summary>The store of this run; a default one until the app starts (designer, tests).</summary>
    public static SettingsStore Current { get; set; } = new(new JsonFile("", false), new JsonFile("", false), isPersistent: false);

    public AppSettings Settings { get; private set; }

    public ShortcutMap Shortcuts { get; }

    /// <summary>Raised after a setting changed (old, new).</summary>
    public event EventHandler<(AppSettings Old, AppSettings New)>? Changed;

    /// <summary>Raised after a shortcut changed.</summary>
    public event EventHandler? ShortcutsChanged;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var old = Settings;
        var next = change(old);
        if (next == old)
        {
            return;
        }
        Settings = next;
        _settingsFile.Write(next.ToJson());
        Changed?.Invoke(this, (old, next));
    }

    public TResult UpdateShortcuts<TResult>(Func<ShortcutMap, TResult> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var result = change(Shortcuts);
        _shortcutsFile.Write(Shortcuts.ToJson());
        ShortcutsChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>
    /// The shortcut of a registry action as the user set it (null: none). Remove from Album has the Recycle Bin's key:
    /// in an album that key takes files out instead.
    /// </summary>
    public ActionShortcut? Shortcut(ActionId id) =>
        id == ActionId.RemoveFromAlbum ? Shortcut(ActionId.MoveToTrash)
        : ShortcutCommands.Of(id) is { } command ? Shortcuts.Shortcut(command) : ActionRegistry.Shortcut(id);
}

/// <summary>Whether fades and slides run: Windows' animation effects on, and Settings → Reduce motion off.</summary>
internal static class Motion
{
    public static bool Enabled => Platform.Windows.SystemMotion.AnimationsEnabled && !SettingsStore.Current.Settings.ReduceMotion;
}
