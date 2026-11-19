using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Support;

/// <summary>
/// Plays an animated GIF or PNG (macOS MAC-039): frames are composed off the UI thread (<see cref="AnimatedImageReader"/>)
/// and handed to the view as bitmaps, with the file's own delays and loop count; after the last loop the last frame
/// stays. Two bitmaps take turns, so the one on screen is never written. Nothing plays while Windows' animations or the
/// app's motion are off. Dispose stops it; the view lets go of the frame first.
/// </summary>
internal sealed class AnimationPlayer : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly WriteableBitmap?[] _bitmaps = new WriteableBitmap?[2];
    private int _back;

    private AnimationPlayer(string path, Action<Bitmap> show) => _ = RunAsync(path, show);

    /// <summary>The file's type can animate (GIF, PNG); whether this file does is found when it plays.</summary>
    public static bool MayAnimate(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".gif" or ".png" or ".apng";

    /// <summary>
    /// Starts playing <paramref name="path"/> when it is an animation and motion is on; null otherwise.
    /// <paramref name="show"/> gets every frame on the UI thread.
    /// </summary>
    public static AnimationPlayer? Start(string path, Action<Bitmap> show) =>
        MayAnimate(path) && Settings.Motion.Enabled ? new AnimationPlayer(path, show) : null;

    private async Task RunAsync(string path, Action<Bitmap> show)
    {
        var cancel = _stop.Token;
        AnimatedImageReader? reader = null;
        try
        {
            reader = await Task.Run(() => AnimatedImageReader.Open(path), cancel);
            if (reader is null)
            {
                return;
            }
            var canvas = new byte[reader.Width * reader.Height * 4];
            var loops = 0;
            var clock = Stopwatch.StartNew();
            while (!cancel.IsCancellationRequested)
            {
                clock.Restart();
                var delay = await Task.Run(() => reader.Next(canvas), cancel);
                if (cancel.IsCancellationRequested)
                {
                    return;
                }
                show(Fill(canvas, reader.Width, reader.Height));
                if (reader.Index == 0 && reader.LoopCount > 0 && ++loops >= reader.LoopCount)
                {
                    return;
                }
                var wait = delay - clock.Elapsed;
                await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, cancel);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or COMException or IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException)
        {
        }
        finally
        {
            reader?.Dispose();
        }
    }

    /// <summary>The frame into the bitmap not on screen, which then becomes the one shown.</summary>
    private WriteableBitmap Fill(byte[] canvas, int width, int height)
    {
        var bitmap = _bitmaps[_back] ??= new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(canvas, y * width * 4, buffer.Address + y * buffer.RowBytes, width * 4);
            }
        }
        _back = 1 - _back;
        return bitmap;
    }

    public void Dispose()
    {
        _stop.Cancel();
        // After the view has let go of the frame (it does before disposing the player).
        var bitmaps = _bitmaps.ToArray();
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var bitmap in bitmaps)
            {
                bitmap?.Dispose();
            }
            _stop.Dispose();
        }, DispatcherPriority.Background);
    }
}
