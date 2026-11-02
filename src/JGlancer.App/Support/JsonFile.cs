namespace JGlancer.App.Support;

/// <summary>
/// A small settings file next to the library: read once, written on the thread pool, latest text wins, replaced
/// atomically (temporary file, then move). Ephemeral runs (benchmarks, snapshots) never write.
/// </summary>
internal sealed class JsonFile(string path, bool isPersistent)
{
    private readonly Lock _lock = new();
    private string? _pending;
    private bool _writing;

    /// <summary>The file's text, or null when it is missing or unreadable.</summary>
    public string? Read()
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(string json)
    {
        if (!isPersistent)
        {
            return;
        }
        lock (_lock)
        {
            _pending = json;
            if (_writing)
            {
                return;
            }
            _writing = true;
        }
        _ = Task.Run(WriteLoop);
    }

    private void WriteLoop()
    {
        while (true)
        {
            string json;
            lock (_lock)
            {
                if (_pending is null)
                {
                    _writing = false;
                    return;
                }
                json = _pending;
                _pending = null;
            }
            try
            {
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A lost write only costs the next launch a remembered setting.
            }
        }
    }
}
