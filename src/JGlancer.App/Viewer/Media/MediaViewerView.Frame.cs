using System.Globalization;
using Avalonia.Controls;
using JGlancer.Core.Localization;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Viewer.Media;

/// <summary>
/// Save the video frame (W-75): the frame on screen at the video's own size as a PNG next to
/// the video ("clip 01-23.456.png", never over an existing file), or on the clipboard as a picture. P saves, Shift+P
/// copies; the camera button saves, its right-click menu offers both.
/// </summary>
internal sealed partial class MediaViewerView
{
    // A save in flight keeps the player alive: closing the viewer waits for it (PlayerReleased).
    private Task _frameSave = Task.CompletedTask;

    private void SetUpFrameButton()
    {
        ToolTip.SetTip(FrameButton, L.T("Save Frame (P) · Shift+P copies it"));
        FrameButton.Click += (_, _) => _ = SaveFrameAsync(copy: false);
        var save = new MenuItem { Header = L.T("Save Frame"), InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.P) };
        save.Click += (_, _) => _ = SaveFrameAsync(copy: false);
        var copy = new MenuItem { Header = L.T("Copy Frame"), InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.P, Avalonia.Input.KeyModifiers.Shift) };
        copy.Click += (_, _) => _ = SaveFrameAsync(copy: true);
        FrameButton.ContextFlyout = new MenuFlyout { Items = { save, copy } };
    }

    internal Task SaveFrameAsync(bool copy)
    {
        if (!_frameSave.IsCompleted || _model is not { Kind: Core.Model.MediaKind.Video } model || model.Current is not { } item)
        {
            return _frameSave;
        }
        var player = model.Playback.Player;
        var path = copy
            ? Path.Combine(Path.GetTempPath(), "jGlancer", $"frame-{Environment.ProcessId}.png")
            : FramePath(item.Path, model.Playback.CurrentTime);
        _frameSave = Run();
        return _frameSave;

        async Task Run()
        {
            var saved = await Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    return player.SaveFrame(path) && File.Exists(path) && (!copy || ClipboardImage.CopyPng(path));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    return false;
                }
                finally
                {
                    if (copy)
                    {
                        TryDelete(path);
                    }
                }
            });
            MessageRequested?.Invoke(this, !saved ? L.T("Couldn't save the frame.")
                : copy ? L.T("Frame copied.")
                : L.T("Frame saved as “{0}”.", Path.GetFileName(path)));
        }
    }

    /// <summary>"clip 01-23.456.png" beside the video; "(2)", "(3)"… when taken.</summary>
    private static string FramePath(string video, double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var stamp = time.TotalHours >= 1
            ? time.ToString(@"h\-mm\-ss\.fff", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\-ss\.fff", CultureInfo.InvariantCulture);
        var folder = Path.GetDirectoryName(video) ?? "";
        var name = $"{Path.GetFileNameWithoutExtension(video)} {stamp}";
        var path = Path.Combine(folder, name + ".png");
        for (var copy = 2; File.Exists(path); copy++)
        {
            path = Path.Combine(folder, $"{name} ({copy}).png");
        }
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
