using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using JGlancer.Core.Launch;

namespace JGlancer.App.Services;

/// <summary>
/// One app per library folder (W11): the first launch owns a session mutex and listens on a named pipe; later launches
/// (Explorer's Open With, its context menu, the exe again) send their request there and quit, so files open in the
/// window that is already there. The pipe is for the current user only. Developer runs with their own data folder are
/// instances of their own.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipe;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, string pipe)
    {
        _mutex = mutex;
        _pipe = pipe;
    }

    /// <summary>
    /// This process as the app for <paramref name="dataDirectory"/>, or null after the request went to the one that
    /// already runs (then this process ends).
    /// </summary>
    public static SingleInstance? Claim(string dataDirectory, LaunchArguments launch)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        ArgumentNullException.ThrowIfNull(launch);
        var name = "jGlancer-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory).ToUpperInvariant())))[..16];
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + name, out var created);
        if (created)
        {
            return new SingleInstance(mutex, name);
        }
        mutex.Dispose();
        if (Send(name, launch))
        {
            return null;
        }
        // The other one is going away (its pipe is closed): this one becomes the app.
        return new SingleInstance(new Mutex(false, @"Local\" + name), name);
    }

    /// <summary>Requests from later launches, on a pool thread.</summary>
    public event EventHandler<LaunchArguments>? Received;

    public void Listen() => _ = Task.Run(ListenAsync);

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var text = await reader.ReadToEndAsync(_stop.Token).ConfigureAwait(false);
                Received?.Invoke(this, LaunchArguments.Deserialize(text));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client that went away mid-message: wait for the next one.
            }
        }
    }

    private static bool Send(string pipe, LaunchArguments launch)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            // Explorer gave this launch the right to come to the front: the running app may use it for its window.
            Platform.Windows.ForegroundRights.PassOn();
            var bytes = Encoding.UTF8.GetBytes(launch.Serialize());
            client.Write(bytes);
            client.Flush();
            return true;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool _disposed;

    /// <summary>Stops listening; safe to call again (the app's exit may report twice).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _stop.Cancel();
        _mutex.Dispose();
        _stop.Dispose();
    }
}
