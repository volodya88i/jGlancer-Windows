using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using JGlancer.App.Actions;
using JGlancer.App.Library;
using JGlancer.App.Services;
using JGlancer.App.Support;
using JGlancer.App.Viewer.Media;
using JGlancer.Core.Actions;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Core.Settings;
using JGlancer.Design;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Settings;

/// <summary>A section of Settings.</summary>
internal enum SettingsSection
{
    General,
    Appearance,
    Library,
    Playback,
    EditWith,
    Windows,
    Shortcuts,
}

/// <summary>
/// Settings (Ctrl+,; macOS SettingsView, FEATURES §14): sections listed on the left in the sidebar's style, the section
/// on the right. Every change applies at once and is saved; the page shows the settings as they are now.
/// </summary>
internal sealed partial class SettingsWindow : Window
{
    private readonly SettingsStore _store = SettingsStore.Current;
    private readonly LibraryModel _library;
    private readonly OpenWithStore _openWith;
    private readonly StackPanel _sections = new() { Spacing = JGSpacing.Xxs, Margin = new Thickness(JGSpacing.Xs, JGSpacing.S + JGSpacing.Xs, JGSpacing.Xs, JGSpacing.Xs) };
    private readonly StackPanel _page = new() { Spacing = JGSpacing.L, Margin = new Thickness(JGSpacing.L) };
    private SettingsSection _section;

    // The XAML-less window needs no loader; the app always passes its parts.
    public SettingsWindow(LibraryModel library, OpenWithStore openWith, SettingsSection section = SettingsSection.General)
    {
        _library = library;
        _openWith = openWith;
        _section = section;
        Title = L.T("Settings");
        Width = JGMetrics.SettingsWidth;
        Height = JGMetrics.SettingsHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://jGlancer/Assets/jGlancer.ico")));
        FontFamily = JGFonts.Text;
        this.Bind(BackgroundProperty, this.GetResourceObservable(JGColor.BgBase.ResourceKey));
        var sidebar = new Border { Width = JGMetrics.SettingsSidebarWidth, Child = _sections };
        sidebar.Bind(Border.BackgroundProperty, sidebar.GetResourceObservable(JGColor.BgPanel.ResourceKey));
        var hairline = new Border { Width = JGMetrics.OutlineWidth };
        hairline.Bind(Border.BackgroundProperty, hairline.GetResourceObservable(JGColor.LineHair.ResourceKey));
        var content = new ScrollViewer { Content = _page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        Grid.SetColumn(hairline, 1);
        Grid.SetColumn(content, 2);
        layout.Children.Add(sidebar);
        layout.Children.Add(hairline);
        layout.Children.Add(content);
        Content = layout;
        _store.Changed += OnSettingsChanged;
        _store.ShortcutsChanged += OnShortcutsChanged;
        library.PropertyChanged += OnLibraryChanged;
        Closed += (_, _) =>
        {
            _store.Changed -= OnSettingsChanged;
            _store.ShortcutsChanged -= OnShortcutsChanged;
            library.PropertyChanged -= OnLibraryChanged;
        };
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Show(section);
    }

    private void OnSettingsChanged(object? sender, (AppSettings Old, AppSettings New) change) => Rebuild();

    private void OnShortcutsChanged(object? sender, EventArgs e) => Rebuild();

    private void OnLibraryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryModel.Roots) && _section == SettingsSection.Library)
        {
            Rebuild();
        }
    }

    public void Show(SettingsSection section)
    {
        _section = section;
        _sections.Children.Clear();
        foreach (var item in Enum.GetValues<SettingsSection>())
        {
            var row = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            if (this.TryFindResource("JG.sidebarRow", out var theme) && theme is Avalonia.Styling.ControlTheme sidebarRow)
            {
                row.Theme = sidebarRow;
            }
            row.Classes.Set("selected", item == section);
            var label = SettingsUi.Text(SectionTitle(item), "body", item == section ? JGColor.TextPrimary : JGColor.TextSecondary);
            var glyph = new JGIcon { Data = SectionIcon(item), IsFilled = true, VerticalAlignment = VerticalAlignment.Center };
            glyph.Bind(JGIcon.ForegroundProperty, glyph.GetResourceObservable((item == section ? JGColor.TextPrimary : JGColor.TextSecondary).ResourceKey));
            row.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.Xs, Children = { glyph, label } };
            AutomationProperties.SetName(row, SectionTitle(item));
            var chosen = item;
            row.Click += (_, _) => Show(chosen);
            _sections.Children.Add(row);
        }
        Rebuild();
    }

    private void Rebuild()
    {
        _page.Children.Clear();
        var controls = _section switch
        {
            SettingsSection.General => General(),
            SettingsSection.Appearance => Appearance(),
            SettingsSection.Library => LibraryPage(),
            SettingsSection.Playback => Playback(),
            SettingsSection.EditWith => EditWith(),
            SettingsSection.Windows => WindowsPage(),
            _ => Shortcuts(),
        };
        _page.Children.AddRange(controls);
    }

    private static string SectionTitle(SettingsSection section) => section switch
    {
        SettingsSection.General => L.T("General"),
        SettingsSection.Appearance => L.T("Appearance"),
        SettingsSection.Library => L.T("Library"),
        SettingsSection.Playback => L.T("Playback"),
        SettingsSection.EditWith => L.T("Edit With|settings"),
        SettingsSection.Windows => L.T("Windows"),
        _ => L.T("Shortcuts"),
    };

    private static string SectionIcon(SettingsSection section) => section switch
    {
        SettingsSection.General => JGSolidIcons.General,
        SettingsSection.Appearance => JGSolidIcons.Appearance,
        SettingsSection.Library => JGSolidIcons.Library,
        SettingsSection.Playback => JGSolidIcons.Playback,
        SettingsSection.EditWith => JGSolidIcons.EditWith,
        SettingsSection.Windows => JGSolidIcons.WindowsSection,
        _ => JGSolidIcons.Shortcuts,
    };

    private AppSettings S => _store.Settings;

    private void Set(Func<AppSettings, AppSettings> change) => _store.Update(change);

    // MARK: General

    private List<Control> General() =>
    [
        SettingsUi.Group(L.T("Language"),
            SettingsUi.Segmented(L.T("Language"), [(L.T("System"), AppLanguage.System), ("English", AppLanguage.English), ("Русский", AppLanguage.Russian)],
                S.Language, language => Set(s => s with { Language = language })),
            SettingsUi.Text(L.T("Applies the next time jGlancer opens."), "body", JGColor.TextTertiary, wraps: true)),
        Updates(),
    ];

    private string? _updateStatus;

    /// <summary>Settings → General → Updates (W-101).</summary>
    private StackPanel Updates()
    {
        var check = new Button { Content = L.T("Check Now"), HorizontalAlignment = HorizontalAlignment.Left };
        check.Click += async (_, _) => await CheckForUpdatesAsync(check);
        var group = SettingsUi.Group(L.T("Updates"),
            SettingsUi.Toggle(L.T("Look for new versions automatically"), S.CheckForUpdates, on => Set(s => s with { CheckForUpdates = on })),
            SettingsUi.Text(L.T("Once a day jGlancer asks GitHub for its latest version. Nothing about your files or library is sent."), "body", JGColor.TextTertiary, wraps: true),
            check);
        if (_updateStatus is { } status)
        {
            group.Children.Add(SettingsUi.Text(status, "body", JGColor.TextSecondary, wraps: true));
        }
        return group;
    }

    private async Task CheckForUpdatesAsync(Button check)
    {
        check.IsEnabled = false;
        try
        {
            var release = await UpdateService.LatestAsync(CancellationToken.None);
            Set(s => s with { LastUpdateCheck = DateTimeOffset.UtcNow });
            if (release is not null && Core.Updates.AppUpdates.IsNewer(release, Shell.AboutWindow.Version))
            {
                _updateStatus = null;
                await Shell.UpdateWindow.ShowAsync(this, release);
            }
            else
            {
                _updateStatus = L.T("You have the latest version ({0}).", Shell.AboutWindow.Version);
            }
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        {
            _updateStatus = L.T("Couldn't check for updates. Check the connection and try again.");
        }
        check.IsEnabled = true;
        Rebuild();
    }

    // MARK: Appearance

    private List<Control> Appearance()
    {
        var matching = AppearancePresets.Matching(S);
        var look = SettingsUi.Group(L.T("Look"), PresetCards.Build(matching, isLarge: false, preset => Set(s => preset.Applied(s))));
        if (matching is null)
        {
            look.Children.Add(SettingsUi.Text(L.T("Your own mix."), "body", JGColor.TextTertiary));
        }
        return
        [
            look,
            AccentColorGroup(),
            SettingsUi.Group(L.T("Effects"),
                SettingsUi.Toggle(L.T("Frosted glass in menus and panels"), S.FrostedGlass, on => Set(s => s with { FrostedGlass = on })),
                SettingsUi.Toggle(L.T("Blurred cover behind audio"), S.CoverBackdrop, on => Set(s => s with { CoverBackdrop = on })),
                SettingsUi.Toggle(L.T("Blend the backdrop into the next song's"), S.BackdropCrossfade, on => Set(s => s with { BackdropCrossfade = on })),
                SettingsUi.Toggle(L.T("Show the next file while swiping"), S.SwipeAnimation, on => Set(s => s with { SwipeAnimation = on })),
                SettingsUi.Toggle(L.T("Animate the library grid"), S.GridAnimation, on => Set(s => s with { GridAnimation = on })),
                SettingsUi.Toggle(L.T("Reduce motion"), S.ReduceMotion, on => Set(s => s with { ReduceMotion = on }))),
            SettingsUi.Group(L.T("Filmstrip"),
                SettingsUi.Segmented(L.T("Filmstrip"), [(L.T("Button"), FilmstripReveal.Button), (L.T("Automatic"), FilmstripReveal.Automatic)],
                    S.FilmstripReveal, reveal => Set(s => s with { FilmstripReveal = reveal }))),
            SettingsUi.Group(L.T("Kind Filters"),
                SettingsUi.Segmented(L.T("Kind Filters"), [(L.T("Words"), KindFilterStyle.Labels), (L.T("Icons"), KindFilterStyle.Icons)],
                    S.KindFilterStyle, style => Set(s => s with { KindFilterStyle = style }))),
            SettingsUi.Group(L.T("Theme"),
                SettingsUi.Segmented(L.T("Theme"), [(L.T("System"), AppTheme.System), (L.T("Dark"), AppTheme.Dark), (L.T("Black|theme"), AppTheme.Black), (L.T("Light"), AppTheme.Light)],
                    S.Theme, theme => Set(s => s with { Theme = theme }))),
            SettingsUi.Group(L.T("Decor"),
                SettingsUi.Toggle(L.T("Crosshair marks on selected files"), S.CrosshairMarks, on => Set(s => s with { CrosshairMarks = on })),
                SettingsUi.Labeled(L.T("Audio player"), SettingsUi.Segmented(L.T("Audio player"),
                    [(L.T("Waveform|audio"), WaveformStyle.Narrow), (L.T("Timeline|waveform"), WaveformStyle.Timeline)],
                    S.Waveform, style => Set(s => s with { Waveform = style }))),
                SettingsUi.Labeled(L.T("Video player"), SettingsUi.Segmented(L.T("Video player"),
                    [(L.T("Waveform|audio"), WaveformStyle.Narrow), (L.T("Timeline|waveform"), WaveformStyle.Timeline)],
                    S.VideoWaveform, style => Set(s => s with { VideoWaveform = style })))),
        ];
    }

    // MARK: Library

    private List<Control> LibraryPage()
    {
        var folders = SettingsUi.Group(L.T("Folders"));
        foreach (var root in _library.Roots)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = JGSpacing.Xs, MinHeight = JGMetrics.ControlHeightCompact };
            var glyph = new JGIcon { Data = JGIconData.HardDrive, VerticalAlignment = VerticalAlignment.Center };
            glyph.Bind(JGIcon.ForegroundProperty, glyph.GetResourceObservable(JGColor.TextSecondary.ResourceKey));
            var path = SettingsUi.Text(root.Path);
            path.TextTrimming = Avalonia.Media.TextTrimming.PrefixCharacterEllipsis;
            var remove = SettingsUi.IconButton(JGIconData.X, L.T("Remove from Library"), async () =>
            {
                if (await ConfirmDialog.ShowAsync(this, L.T("Remove “{0}” from the library?", Path.GetFileName(root.Path.TrimEnd('\\')) is { Length: > 0 } name ? name : root.Path),
                    L.T("Files stay where they are."), L.T("Remove")))
                {
                    await _library.RemoveRootAsync(root.Id);
                }
            });
            Grid.SetColumn(path, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(glyph);
            row.Children.Add(path);
            row.Children.Add(remove);
            folders.Children.Add(row);
        }
        var add = new Button { Content = L.T("Add Folder…"), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += async (_, _) => await _library.AddFoldersAsync(await FolderPicker.ChooseAsync(this));
        folders.Children.Add(add);

        var used = SettingsUi.Text("…", "body", JGColor.TextSecondary);
        _ = ShowCacheUsageAsync(used);
        var cap = SettingsUi.Segmented(L.T("At most"), [.. AppSettings.CacheChoices.Select(g => (Gigabytes(g), g))],
            S.ThumbnailCacheGigabytes, gigabytes => Set(s => s with { ThumbnailCacheGigabytes = gigabytes }));
        var clear = new Button { Content = L.T("Clear Cache") };
        clear.Click += async (_, _) =>
        {
            await Task.Run(_library.Services.Thumbnails.RemoveAll);
            await ShowCacheUsageAsync(used);
        };
        var cacheRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.S, Children = { SettingsUi.Text(L.T("At most"), "body", JGColor.TextSecondary), cap } };
        var usageRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.S, Children = { used, clear } };
        var sequences = SettingsUi.Group(L.T("Image Sequences"),
            SettingsUi.Toggle(L.T("Fold numbered frames into one stack"), S.GroupSequences, on => Set(s => s with { GroupSequences = on })),
            SettingsUi.Text(L.T("Five or more photos named with numbers one after another (render_001 … render_090) show as a stack; a click on its count unfolds it."),
                "body", JGColor.TextTertiary, wraps: true));
        var preview = SettingsUi.Group(L.T("Preview"),
            SettingsUi.Toggle(L.T("Show camera, codec and other details"), S.PreviewDetails, on => Set(s => s with { PreviewDetails = on })));
        return [folders, sequences, preview, SettingsUi.Group(L.T("Thumbnail Cache"), cacheRow, usageRow), StemModels()];
    }

    /// <summary>
    /// The stem splitter (W-85, W-92): which model splits (compact or full precision), and the downloaded models, each
    /// with its size and Remove.
    /// </summary>
    private StackPanel StemModels()
    {
        var quality = SettingsUi.Segmented(L.T("Model"),
            [(L.T("Compact (91 MB)"), false), (L.T("Full (174 MB)"), true)], S.StemModelFull, full => Set(s => s with { StemModelFull = full }));
        var group = SettingsUi.Group(L.T("Stem Splitting"), quality,
            SettingsUi.Text(L.T("The compact model keeps its numbers at half precision and sounds the same (the difference is far below hearing); the full one is the original, twice the download."),
                "body", JGColor.TextTertiary, wraps: true));
        var folder = _library.Services.Paths.Models;
        foreach (var model in Core.Stems.StemModelInfo.All)
        {
            var path = JGlancer.Stems.StemModelDownload.PathIn(folder, model);
            var name = SettingsUi.Text((model.Mode, model.IsFull) switch
            {
                (Core.Stems.StemMode.Six, true) => L.T("Full model for 6 stems"),
                (Core.Stems.StemMode.Six, false) => L.T("Compact model for 6 stems"),
                (_, true) => L.T("Full model for 4 stems"),
                _ => L.T("Compact model for 4 stems"),
            });
            var size = SettingsUi.Text("", "mono", JGColor.TextSecondary);
            var remove = new Button { Content = L.T("Remove") };
            void Show()
            {
                var there = File.Exists(path);
                size.Text = there ? ByteFormat.Format(new FileInfo(path).Length) : L.T("Not downloaded");
                remove.IsVisible = there;
            }
            remove.Click += (_, _) =>
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // In use by a split: it stays.
                }
                Show();
            };
            Show();
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = JGSpacing.S, MinHeight = JGMetrics.ControlHeight };
            row.Children.Add(name);
            Grid.SetColumn(size, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(size);
            row.Children.Add(remove);
            group.Children.Add(row);
        }
        group.Children.Add(SettingsUi.Text(L.T("Downloaded the first time you split a song (Split into Stems… in a song's menu)."), "body", JGColor.TextTertiary, wraps: true));
        return group;
    }

    private static string Gigabytes(int gigabytes) => L.T("{0} GB", gigabytes.ToString(CultureInfo.CurrentCulture));

    private async Task ShowCacheUsageAsync(TextBlock used)
    {
        var bytes = await Task.Run(_library.Services.Cache.UsedBytes);
        used.Text = ByteFormat.Format(bytes);
    }

    // MARK: Playback

    private List<Control> Playback()
    {
        var file = _library.Services.PlaybackPreferences;
        var preferences = MediaPlayback.SharedPreferences(file);
        return
        [
            SettingsUi.Group(L.T("Playback"),
                SettingsUi.Toggle(L.T("Play video and audio in Quick Preview"), S.AutoplayInPreview, on => Set(s => s with { AutoplayInPreview = on })),
                SettingsUi.Toggle(L.T("Keep Pitch"), preferences.PreservesPitch, on =>
                {
                    preferences.PreservesPitch = on;
                    MediaPlayback.SavePreferences(file);
                })),
            SettingsUi.Group(L.T("Segments"),
                SettingsUi.Toggle(L.T("Cut video exactly at the brackets (encodes it again)"), preferences.SegmentExactVideo, on =>
                {
                    preferences.SegmentExactVideo = on;
                    MediaPlayback.SavePreferences(file);
                }),
                SettingsUi.Toggle(L.T("Keep WAV, AIFF, FLAC and MP3 segments in their format (otherwise M4A)"), preferences.SegmentKeepsAudioFormat, on =>
                {
                    preferences.SegmentKeepsAudioFormat = on;
                    MediaPlayback.SavePreferences(file);
                }),
                SettingsUi.Toggle(L.T("Fade the Ends"), preferences.SegmentFades, on =>
                {
                    preferences.SegmentFades = on;
                    MediaPlayback.SavePreferences(file);
                }),
                SettingsUi.Toggle(L.T("Zoom the timeline with the wheel alone (without Ctrl)"), S.TimelineWheelZoom,
                    on => Set(s => s with { TimelineWheelZoom = on }))),
        ];
    }

    // MARK: Edit With

    private List<Control> EditWith()
    {
        var controls = new List<Control>
        {
            // The key as Settings → Shortcuts has it now.
            SettingsUi.Text(_store.Shortcut(Core.Actions.ActionId.EditInPrimaryApp) is { } key
                ? L.T("Programs listed first in Open With. The starred one opens with {0}.", Display(key))
                : L.T("Programs listed first in Open With. The starred one is the primary program."), "body", JGColor.TextSecondary, wraps: true),
        };
        foreach (var kind in new[] { MediaKind.Photo, MediaKind.Video, MediaKind.Audio })
        {
            var section = SettingsUi.Group(kind switch { MediaKind.Photo => L.T("Photo"), MediaKind.Video => L.T("Video"), _ => L.T("Audio") });
            var primary = _openWith.Settings.Primary(kind);
            foreach (var app in _openWith.Settings.AppsFor(kind))
            {
                section.Children.Add(ProgramRow(app, kind, primary is not null && app.IsSame(primary)));
            }
            var add = new Button { Content = L.T("Add Program…"), HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += async (_, _) => await AddProgramsAsync(kind);
            section.Children.Add(add);
            controls.Add(SettingsUi.Card(section));
        }
        return controls;
    }

    private Grid ProgramRow(ExternalApp app, MediaKind kind, bool isPrimary)
    {
        var installed = File.Exists(app.Path);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = JGSpacing.S, MinHeight = JGMetrics.ControlHeightCompact };
        if (installed && ShellOpenWith.Icon(app.Path, (int)Math.Ceiling(JGMetrics.IconSize * RenderScaling)) is { } icon)
        {
            row.Children.Add(new Image { Source = ThumbnailCache.ToBitmap(icon), Width = JGMetrics.IconSize, Height = JGMetrics.IconSize, VerticalAlignment = VerticalAlignment.Center });
        }
        var name = SettingsUi.Text(installed ? app.Name : L.T("{0} (not installed)", app.Name), "body", installed ? JGColor.TextPrimary : JGColor.TextTertiary);
        ToolTip.SetTip(name, app.Path);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var star = SettingsUi.IconButton(JGIconData.Star, L.T("Primary App"), () =>
        {
            _openWith.Update(s => s.SetPrimary(app.Path, kind));
            Rebuild();
        },
            isPrimary ? JGColor.AccentText : JGColor.TextTertiary);
        star.Classes.Set("on", isPrimary);
        Grid.SetColumn(star, 2);
        row.Children.Add(star);
        var remove = SettingsUi.IconButton(JGIconData.X, L.T("Remove"), () =>
        {
            _openWith.Update(s => s.Remove(app.Path, kind));
            Rebuild();
        });
        Grid.SetColumn(remove, 3);
        row.Children.Add(remove);
        return row;
    }

    private async Task AddProgramsAsync(MediaKind kind)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var chosen = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Add Program…"),
            AllowMultiple = true,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(new Uri(programs + Path.DirectorySeparatorChar)),
            FileTypeFilter = [new FilePickerFileType(L.T("Programs")) { Patterns = ["*.exe"] }],
        });
        var paths = chosen.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0)
        {
            _openWith.Update(settings =>
            {
                foreach (var path in paths)
                {
                    settings.Add(new ExternalApp(path, ShellOpenWith.ProgramName(path)), kind);
                }
            });
            Rebuild();
        }
    }
}
