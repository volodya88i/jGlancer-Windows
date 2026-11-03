using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using JGlancer.App.Developer;
using JGlancer.App.Library;
using JGlancer.App.Services;
using JGlancer.App.Settings;
using JGlancer.App.Shell;
using JGlancer.App.Support;
using JGlancer.Core.Settings;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App;

/// <summary>The application: theme, language, services, the library window, Quick Preview from Explorer.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Services and the Explorer preview are disposed in Stop, when the app exits.")]
internal sealed partial class App : Application
{
    private AppServices? _services;
    private LibraryModel? _model;
    private uint _captionColor;

    public override void Initialize()
    {
        Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<Avalonia.Controls.Window>((window, _) => PaintCaption(window));
        // Fluent supplies templates for the controls the design system does not restyle yet (scroll bars, fields);
        // JGTheme's control themes and token brushes come after it and win.
        Styles.Add(new FluentTheme());
        JGTheme.Install(this);
        var options = DeveloperOptions.Current;
        var paths = AppPaths.For(options);
        Directory.CreateDirectory(paths.DataDirectory);
        var persistent = !options.IsEphemeral;
        var settings = new SettingsStore(new JsonFile(paths.Settings, persistent), new JsonFile(paths.Shortcuts, persistent), persistent, options.Onboarding);
        SettingsStore.Current = settings;
        // The language is read once: text in the windows is set as they load (applies at the next start, as macOS).
        L.Use(options.Language ?? settings.Settings.Language switch
        {
            AppLanguage.English => "en",
            AppLanguage.Russian => "ru",
            _ => null,
        });
        ApplyTheme();
        ApplyAccent();
        settings.Changed += (_, change) =>
        {
            if (change.Old.Theme != change.New.Theme)
            {
                ApplyTheme();
            }
            if (change.Old.AccentColor != change.New.AccentColor)
            {
                ApplyAccent();
            }
        };
        if (PlatformSettings is { } platform)
        {
            platform.ColorValuesChanged += (_, _) => ApplyTheme();
        }
    }

    /// <summary>
    /// Settings → Theme: dark by default (macOS D-20), black (W-97), light, or Windows' app mode. Windows contrast themes turn on the
    /// Increase Contrast variants (D-19). The developer <c>--theme</c> wins.
    /// </summary>
    private void ApplyTheme()
    {
        var theme = DeveloperOptions.Current.Theme;
        var colors = PlatformSettings?.GetColorValues();
        var isBlack = theme is not null
            ? theme.StartsWith("black", StringComparison.Ordinal)
            : SettingsStore.Current.Settings.Theme == AppTheme.Black;
        var isDark = theme is not null
            ? !theme.StartsWith("light", StringComparison.Ordinal)
            : SettingsStore.Current.Settings.Theme switch
            {
                AppTheme.Light => false,
                AppTheme.System => colors?.ThemeVariant != PlatformThemeVariant.Light,
                _ => true,
            };
        var increasedContrast = theme?.EndsWith("-hc", StringComparison.Ordinal) == true
            || colors?.ContrastPreference == ColorContrastPreference.High;
        RequestedThemeVariant = isBlack ? JGTheme.BlackVariant(increasedContrast) : JGTheme.Variant(isDark, increasedContrast);
        _captionColor = isBlack ? JGColor.BgPanel.BlackValue(increasedContrast) : JGColor.BgPanel.Value(isDark, increasedContrast);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { } lifetime)
        {
            foreach (var window in lifetime.Windows)
            {
                PaintCaption(window);
            }
        }
    }

    /// <summary>System title bars (Setup, Settings, dialogs) in the color of the app's own.</summary>
    private void PaintCaption(Avalonia.Controls.Window window)
    {
        if (window.TryGetPlatformHandle()?.Handle is { } handle)
        {
            Platform.Windows.WindowCaption.SetColor(handle, _captionColor);
        }
    }

    /// <summary>Settings → Appearance → Accent Color (W-93); open windows repaint, self-drawn controls included.</summary>
    private void ApplyAccent()
    {
        var text = DeveloperOptions.Current.Accent ?? SettingsStore.Current.Settings.AccentColor;
        JGTheme.SetAccent(this, AccentColorText.TryParse(text, out var chosen) ? chosen : null);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { } lifetime)
        {
            foreach (var window in lifetime.Windows)
            {
                foreach (var visual in window.GetVisualDescendants())
                {
                    visual.InvalidateVisual();
                }
            }
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { } setupLifetime && Setup.SetupMode.Current is { } setup)
        {
            // Installing or removing: that window only; the library never starts.
            var setupWindow = new Setup.SetupWindow(setup);
            setupLifetime.MainWindow = setupWindow;
            setupLifetime.Exit += (_, _) => setup.CleanUpAfterExit();
            if (DeveloperOptions.Current.Snapshot is { } setupShot)
            {
                // The window as it opens, rendered offscreen; nothing is installed.
                setupWindow.Opened += (_, _) => LaunchProbe.SnapshotSoon(setupWindow, setupShot);
            }
            base.OnFrameworkInitializationCompleted();
            return;
        }
        Support.PopupGlass.Install();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            var options = DeveloperOptions.Current;
            var paths = AppPaths.For(options);
            Platform.Windows.WindowsThumbnailGenerator.UsesWindowsCache = !options.NoWindowsThumbnailCache;
            JGScrubber.MotionAllowed = () => Settings.Motion.Enabled;
            _services = AppServices.Start(paths, !options.IsEphemeral);
            // Settings → Library → Thumbnail Cache: the cap on disk, now and when it changes.
            var cache = _services.Cache;
            cache.SetLimit(SettingsStore.Current.Settings.ThumbnailCacheGigabytes * (1L << 30));
            SettingsStore.Current.Changed += (_, change) =>
            {
                if (change.Old.ThumbnailCacheGigabytes != change.New.ThumbnailCacheGigabytes)
                {
                    _ = Task.Run(() => cache.SetLimit(change.New.ThumbnailCacheGigabytes * (1L << 30)));
                }
            };
            // The app decides when it ends: the library window may come later than the start (Quick Preview from
            // Explorer, the background helper) or go while the helper stays.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += (_, _) => _exiting = true;
            desktop.Exit += (_, _) => Stop();
            if (Program.Instance is { } instance)
            {
                instance.Received += (_, request) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = HandleLaunchAsync(request));
                instance.Listen();
                desktop.Exit += (_, _) => instance.Dispose();
            }
            ApplySpaceHelper();
            if (!options.IsEphemeral && Environment.ProcessPath is { } executable)
            {
                // An Explorer registration from before 0.3.4 moves to where Windows' Default Apps finds it (W-80).
                var texts = DefaultApps.Texts;
                _ = Task.Run(() => Platform.Windows.ExplorerRegistration.ForCurrentUser(executable).RepairIfMisplaced(texts));
            }
            SettingsStore.Current.Changed += (_, change) =>
            {
                if (change.Old.ExplorerSpacePreview != change.New.ExplorerSpacePreview)
                {
                    ApplySpaceHelper();
                }
                if (_spaceHelper is { } helper)
                {
                    helper.FollowsClicks = change.New.ExplorerPreviewFollowsClicks;
                }
            };
            var launch = options.Launch;
            if (launch.Background)
            {
                // Started with Windows for Space in Explorer: no window; without the setting there is nothing to do.
                if (_spaceHelper is null)
                {
                    // Once the main loop runs (it cannot end before it started).
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
                }
            }
            else if (launch.Preview is { } file)
            {
                // "Quick Preview in jGlancer": the preview alone; the app quits with it (macOS D-69).
                _ = ShowExplorerPreviewAsync([file], file, activate: true);
            }
            else
            {
                OpenMainWindow(launch.Paths);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _exiting;
    private MainWindow? _window;
    private Preview.ExplorerPreview? _explorerPreview;

    /// <summary>
    /// The library window. Launched for a media file it is the viewer alone and the library loads only when asked
    /// (D-65); otherwise the library starts (a folder given joins it and shows).
    /// </summary>
    private MainWindow OpenMainWindow(IReadOnlyList<string> paths)
    {
        var options = DeveloperOptions.Current;
        var services = _services!;
        var model = new LibraryModel(services, new ViewStateStore(AppPaths.For(options).ViewState, !options.IsEphemeral));
        _model = model;
        DeveloperHooks.Prepare(model, options);
        var window = new MainWindow(model);
        if (options.WindowSize is { } size)
        {
            window.Width = size.Width;
            window.Height = size.Height;
        }
        AttachDeveloperChecks(window, model, options);
        _window = window;
        _desktop!.MainWindow = window;
        window.Closed += (_, _) => MainWindowClosed(window);
        if (paths.Any(p => File.Exists(p) && Core.Scanning.MediaClassifier.Classify(p) is not null))
        {
            window.BeginViewerOnly();
            window.LibraryNeeded += (_, _) => _ = StartAsync(model, services, options);
            window.Opened += async (_, _) => await window.OpenPathsAsync(paths);
        }
        else
        {
            _ = StartAsync(model, services, options, paths);
        }
        if (!options.IsEphemeral)
        {
            var checkedOnce = false;
            window.Opened += (_, _) =>
            {
                if (!checkedOnce)
                {
                    checkedOnce = true;
                    _ = UpdateService.CheckAtStartAsync(window);
                }
            };
        }
        window.Show();
        return window;
    }

    private static void AttachDeveloperChecks(MainWindow window, LibraryModel model, DeveloperOptions options)
    {
        LaunchProbe.Attach(window, options);
        DeveloperHooks.Attach(window, model, options);
        if (options.FileOpsCheck is { } fileOpsLog)
        {
            FileOpsCheck.Attach(window, model, fileOpsLog);
        }
        if (options.AlbumCheck is { } albumLog)
        {
            AlbumCheck.Attach(window, model, albumLog);
        }
        if (options.FlagCheck is { } flagLog)
        {
            FlagCheck.Attach(window, model, flagLog);
        }
        if (options.UpdateCheck is { } updateLog)
        {
            UpdateCheck.Attach(window, updateLog);
        }
        if (options.AccentCheck is { } accentLog)
        {
            AccentCheck.Attach(window, accentLog);
        }
        if (options.OpenAlbum)
        {
            model.AlbumsChanged += (_, _) =>
            {
                if (model.State.AlbumId is null && model.Albums.Count > 0)
                {
                    model.SelectAlbum(model.Albums[0].Id);
                }
            };
        }
        if (options.Benchmark is { } benchmark)
        {
            DeveloperHooks.Benchmark(window, benchmark);
        }
    }

    /// <summary>The library window closed: its library stops; the app ends unless something else still needs it.</summary>
    private void MainWindowClosed(MainWindow window)
    {
        if (!ReferenceEquals(window, _window))
        {
            return;
        }
        _window = null;
        _model?.Stop();
        _model = null;
        ShutDownIfIdle();
    }

    /// <summary>Nothing left on screen and nothing to wait for: the app ends.</summary>
    private void ShutDownIfIdle()
    {
        if (!_exiting && _window is null && _explorerPreview?.IsOpen != true && _spaceHelper is null)
        {
            _desktop?.Shutdown();
        }
    }

    /// <summary>Quick Preview of files from Explorer, without the library window.</summary>
    private async Task ShowExplorerPreviewAsync(IReadOnlyList<string> files, string focus, bool activate)
    {
        if (_explorerPreview is null)
        {
            _explorerPreview = new Preview.ExplorerPreview(_services!);
            // Open in Viewer: the file opens in the app's window, as from Explorer.
            _explorerPreview.OpenRequested += (_, path) => _ = HandleLaunchAsync(new Core.Launch.LaunchArguments([path], null, false));
            _explorerPreview.Dismissed += (_, _) =>
            {
                _spaceHelper?.PreviewChanged(open: false);
                ShutDownIfIdle();
            };
        }
        await _explorerPreview.ShowAsync(files, focus, activate);
        _spaceHelper?.PreviewChanged(_explorerPreview.IsOpen);
        if (DeveloperOptions.Current.LaunchLog is { } log)
        {
            await Task.Delay(1500);
            File.AppendAllText(log, $"explorerPreview open={_explorerPreview.IsOpen} file={Path.GetFileName(_explorerPreview.CurrentPath)} libraryWindow={_window is not null}" + Environment.NewLine);
        }
        if (!_explorerPreview.IsOpen)
        {
            // Not a media file: nothing to show.
            ShutDownIfIdle();
        }
    }

    private static async Task StartAsync(LibraryModel model, AppServices services, DeveloperOptions options, IReadOnlyList<string>? folders = null)
    {
        await model.StartAsync();
        if (options.AddFolder is { } folder)
        {
            await model.AddFoldersAsync([folder], quietIfPresent: true);
        }
        // A folder given at launch (Explorer's "Open in jGlancer") joins the library and shows.
        if (folders?.FirstOrDefault(Directory.Exists) is { } given && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
        {
            await window.OpenPathsAsync([given]);
        }
        _ = Task.Run(Platform.Windows.SegmentExporter.RemoveLeftovers);
        if (!options.IsEphemeral)
        {
            // The daily backup runs after start, off the UI thread (macOS D-66).
            await Task.Run(services.BackupAsync);
        }
    }

    /// <summary>A later launch's request: a Quick Preview, or files opened in the library window (made if needed), which comes to the front.</summary>
    private async Task HandleLaunchAsync(Core.Launch.LaunchArguments request)
    {
        if (request.Preview is { } file)
        {
            await ShowExplorerPreviewAsync([file], file, activate: true);
        }
        else if (!request.Background)
        {
            if (_window is not { } window)
            {
                window = OpenMainWindow(request.Paths);
            }
            else
            {
                if (window.WindowState == Avalonia.Controls.WindowState.Minimized)
                {
                    window.WindowState = Avalonia.Controls.WindowState.Normal;
                }
                window.Show();
                window.Activate();
                if (request.Paths.Count > 0)
                {
                    await window.OpenPathsAsync(request.Paths);
                }
            }
        }
        if (DeveloperOptions.Current.LaunchLog is { } log)
        {
            File.AppendAllText(log, $"received paths={request.Paths.Count} preview={_explorerPreview?.IsOpen == true} viewerOnly={_window?.IsViewerOnly} viewer={_window?.ViewerKind}" + Environment.NewLine);
        }
    }

    private void Stop()
    {
        StopSpaceHelper();
        _explorerPreview?.Dispose();
        _explorerPreview = null;
        _model?.Stop();
        _services?.Dispose();
        _model = null;
        _services = null;
    }
}
