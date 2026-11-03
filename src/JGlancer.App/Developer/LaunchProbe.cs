using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;

namespace JGlancer.App.Developer;

/// <summary>Launch timing and window snapshots for <see cref="DeveloperOptions"/>; does nothing without them.</summary>
internal static class LaunchProbe
{
    public static void Attach(Window window, DeveloperOptions options)
    {
        if (options.LaunchLog is null && options.Snapshot is null)
        {
            return;
        }
        window.Opened += (_, _) => window.RequestAnimationFrame(_ => FirstFrame(window, options));
    }

    private static void FirstFrame(Window window, DeveloperOptions options)
    {
        if (options.LaunchLog is { } log)
        {
            var elapsed = DateTime.Now - Process.GetCurrentProcess().StartTime;
            File.AppendAllText(log, string.Create(CultureInfo.InvariantCulture,
                $"launch.firstFrame {elapsed.TotalMilliseconds:0} ms{Environment.NewLine}"));
        }
        if (options.Snapshot is { } path)
        {
            if (window is Shell.MainWindow main)
            {
                DeveloperHooks.WhenSettled(main, async settled =>
                {
                    // Settings is a window of its own and draws nothing outside it: rendered offscreen, never the screen.
                    if (options.SettingsSection is { } settingsSection
                        && Enum.TryParse<Settings.SettingsSection>(settingsSection, ignoreCase: true, out var settingsPage)
                        && main.OpenSettings(settingsPage) is { } settingsWindow)
                    {
                        await Task.Delay(500);
                        SnapshotSoon(settingsWindow, path);
                        return;
                    }
                    if (options.About)
                    {
                        var about = new Shell.AboutWindow();
                        about.Show(main);
                        await Task.Delay(500);
                        SnapshotSoon(about, path);
                        return;
                    }
                    // The Duplicates window, also its own window: rendered offscreen.
                    if (options.Duplicates && main.ShowDuplicates() is { } duplicates)
                    {
                        await Task.Delay(1500);
                        SnapshotSoon(duplicates, path);
                        return;
                    }
                    if (options.CapturesScreen)
                    {
                        // On top of everything, so the screen capture holds nothing but the app.
                        main.Topmost = true;
                        main.Activate();
                        if (options.ContextMenu)
                        {
                            await main.ShowContextMenuForDeveloperAsync();
                        }
                        else if (options.BatchRename && main.DataContext is Library.LibraryModel model)
                        {
                            model.Selection.SelectAll(model.Order);
                            // The dialog is modal: it stays open for the capture.
                            _ = main.PerformAsync(Core.Actions.ActionId.Rename);
                        }
                        else
                        {
                            await main.PerformAsync(Core.Actions.ActionId.GetInfo);
                        }
                        await Task.Delay(500);
                        // Windows it opened (Get Info, dialogs) go above the topmost main window too.
                        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { } lifetime)
                        {
                            foreach (var opened in lifetime.Windows.Where(w => w != main))
                            {
                                opened.Topmost = true;
                            }
                            await Task.Delay(200);
                        }
                    }
                    if (options.MiniPlayer)
                    {
                        await main.PerformAsync(Core.Actions.ActionId.PlayInMiniPlayer);
                        await Task.Delay(1500);
                    }
                    SnapshotSoon(window, path);
                });
            }
            else
            {
                SnapshotSoon(window, path);
            }
        }
    }

    internal static void SnapshotSoon(Window window, string path)
    {
        // A few frames later fonts and layout have settled.
        var frames = 0;
        void Next(TimeSpan _)
        {
            if (++frames < 5)
            {
                window.RequestAnimationFrame(Next);
                return;
            }
            Save(window, path);
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        window.RequestAnimationFrame(Next);
    }

    private static void Save(Window window, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Menus are windows of their own: only the screen shows them over the window.
        if (DeveloperOptions.Current.CapturesScreen && window.TryGetPlatformHandle()?.Handle is { } screenHandle
            && Platform.Windows.WindowCapture.CaptureScreenArea(screenHandle) is { } screen)
        {
            using var shot = Library.ThumbnailCache.ToBitmap(new Media.Thumbnails.ThumbnailImage(screen.Width, screen.Height, screen.Pixels));
            shot.Save(path, PngBitmapEncoderOptions.Default);
            return;
        }
        // Quick Preview (--quick-preview) is its own window: that one is shown.
        if (window is Shell.MainWindow { IsQuickPreviewOpen: true, QuickPreview: { } quickPreview })
        {
            window = quickPreview;
        }
        // Video is an OpenGL surface an offscreen render cannot see: capture the window as the screen shows it.
        var hasVideo = window is Shell.MainWindow { MediaViewer: not null } or Preview.QuickPreviewWindow
            || window is Shell.MainWindow { Preview.Asset.Kind: Core.Model.MediaKind.Video };
        if (hasVideo && window.TryGetPlatformHandle()?.Handle is { } handle
            && Platform.Windows.WindowCapture.Capture(handle) is { } captured)
        {
            using var shot = Library.ThumbnailCache.ToBitmap(new Media.Thumbnails.ThumbnailImage(captured.Width, captured.Height, captured.Pixels));
            shot.Save(path, PngBitmapEncoderOptions.Default);
            return;
        }
        var scaling = window.RenderScaling;
        var size = new PixelSize((int)(window.ClientSize.Width * scaling), (int)(window.ClientSize.Height * scaling));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(window);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
}
