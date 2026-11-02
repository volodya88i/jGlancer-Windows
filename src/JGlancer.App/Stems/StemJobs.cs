using System.Diagnostics;
using Avalonia.Threading;
using JGlancer.App.FileOps;
using JGlancer.Core.FileOps;
using JGlancer.Core.Localization;
using JGlancer.Core.Stems;
using JGlancer.Platform.Windows;
using JGlancer.Stems;

namespace JGlancer.App.Stems;

/// <summary>One song to split: where to, and which stems.</summary>
/// <param name="FolderIsNew">The folder is made for this split (undo then removes all of it).</param>
/// <param name="FullModel">The full-precision model (W-92) rather than the compact one.</param>
internal sealed record StemJob(string Song, string Folder, bool FolderIsNew, StemChoice Choice, bool FullModel = false);

/// <summary>
/// The stem splits of the window (W-85), one at a time in the background while the app stays usable: decode the song
/// (mpv, 44.1 kHz stereo), run Demucs (<see cref="OnnxStemSeparator"/>, one session for the songs of a run), write the
/// ticked stems as ".partial" files in the folder and rename them into place at the end. Stop (or an error)
/// removes this run's partial files and the folder it made. Done: a toast with Show in Explorer, and Ctrl+Z moves
/// what it made to the Recycle Bin. The original song is never touched. UI thread only, except the work itself.
/// </summary>
internal sealed class StemJobs(string modelFolder, FileOperationsModel fileOps) : IDisposable
{
    private readonly Queue<StemJob> _waiting = new();
    private CancellationTokenSource? _stop;
    private OnnxStemSeparator? _separator;

    /// <summary>The song being split.</summary>
    public StemJob? Current { get; private set; }

    /// <summary>0…1 of the song being split.</summary>
    public double Progress { get; private set; }

    /// <summary>The time left, once known.</summary>
    public TimeSpan? Remaining { get; private set; }

    /// <summary>Songs after the current one.</summary>
    public int Waiting => _waiting.Count;

    /// <summary>A job started, moved on or ended.</summary>
    public event EventHandler? Changed;

    public string ModelFolder => modelFolder;

    public void Enqueue(IEnumerable<StemJob> jobs)
    {
        foreach (var job in jobs)
        {
            _waiting.Enqueue(job);
        }
        if (Current is null)
        {
            // Sets Current before it tells anyone.
            _ = RunAsync();
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Stops the current split and drops the waiting ones.</summary>
    public void Stop()
    {
        _waiting.Clear();
        _stop?.Cancel();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync()
    {
        while (_waiting.TryDequeue(out var job))
        {
            Current = job;
            Progress = 0;
            Remaining = null;
            using var stop = new CancellationTokenSource();
            _stop = stop;
            Changed?.Invoke(this, EventArgs.Empty);
            try
            {
                var made = await SplitAsync(job, stop.Token);
                var count = job.Choice.Stems.Count;
                var message = L.Plural("Split into {0} stems", count);
                var folder = job.Folder;
                fileOps.RegisterCreated(UndoKind.SplitStems, made,
                    new Toast(message, ActionLabel: L.T("Show in Explorer"), Action: () => ShellFiles.ShowInExplorer([folder])));
            }
            catch (OperationCanceledException)
            {
                fileOps.ShowMessage(L.T("Splitting stopped"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
                or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or InvalidOperationException)
            {
                Trace.TraceWarning($"Stem split of {job.Song} failed: {error}");
                fileOps.ShowMessage(L.T("Couldn't split “{0}”", Path.GetFileName(job.Song)));
            }
            _stop = null;
        }
        Current = null;
        _separator?.Dispose();
        _separator = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Splits one song; returns what it made (the folder, or the files in a folder that was there).</summary>
    private async Task<IReadOnlyList<string>> SplitAsync(StemJob job, CancellationToken stop)
    {
        var model = StemModelInfo.For(job.Choice.Mode, job.FullModel);
        var audio = await Mpv.MpvAudioDecoder.StereoAsync(job.Song, model.SampleRate, stop)
            ?? throw (stop.IsCancellationRequested ? new OperationCanceledException(stop) : new InvalidDataException("No audio"));
        if (_separator?.Model != model)
        {
            _separator?.Dispose();
            _separator = null;
            var path = StemModelDownload.PathIn(modelFolder, model);
            _separator = await Task.Run(() => OnnxStemSeparator.Open(path, model), stop);
        }
        var separator = _separator;
        var made = new List<(string Partial, string Final)>();
        var writers = new Dictionary<int, IStemSink>();
        var createdFolder = job.FolderIsNew && !Directory.Exists(job.Folder);
        try
        {
            Directory.CreateDirectory(job.Folder);
            foreach (var stem in job.Choice.Stems)
            {
                var final = FileNames.Vacant(Path.Combine(job.Folder, StemNaming.FileName(job.Song, stem)), File.Exists);
                var partial = Path.Combine(job.Folder, "." + Path.GetFileName(final) + ".partial");
                made.Add((partial, final));
                var file = new FileStream(partial, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                writers[model.Sources.ToList().IndexOf(stem)] = new WaveStemWriter(file, model.SampleRate);
            }
            var clock = Stopwatch.StartNew();
            await Task.Run(() =>
            {
                var (left, right) = Channels(audio);
                StemSplitter.Split(left, right, separator, writers, done => Dispatcher.UIThread.Post(() =>
                {
                    Progress = done;
                    Remaining = done > 0.05 ? TimeSpan.FromSeconds(clock.Elapsed.TotalSeconds * (1 - done) / done) : null;
                    Changed?.Invoke(this, EventArgs.Empty);
                }), stop);
            }, stop);
            foreach (var writer in writers.Values.Cast<WaveStemWriter>())
            {
                writer.Complete();
                writer.Dispose();
            }
            writers.Clear();
            foreach (var (partial, final) in made)
            {
                File.Move(partial, final);
            }
            return createdFolder ? [job.Folder] : [.. made.Select(m => m.Final)];
        }
        catch
        {
            foreach (var writer in writers.Values.Cast<WaveStemWriter>())
            {
                writer.Dispose();
            }
            foreach (var (partial, _) in made)
            {
                TryDelete(partial);
            }
            if (createdFolder && Directory.Exists(job.Folder) && !Directory.EnumerateFileSystemEntries(job.Folder).Any())
            {
                Directory.Delete(job.Folder);
            }
            throw;
        }
    }

    private static (float[] Left, float[] Right) Channels(Core.Playback.PcmAudio audio)
    {
        var frames = (int)audio.Frames;
        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(audio.Data);
        var left = new float[frames];
        var right = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            left[i] = samples[2 * i];
            right[i] = samples[2 * i + 1];
        }
        return (left, right);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _stop?.Cancel();
        _separator?.Dispose();
    }
}
