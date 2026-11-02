using JGlancer.App.Support;
using JGlancer.Core.Model;

namespace JGlancer.App.Library;

/// <summary>Saves and restores <see cref="LibraryViewState"/> as JSON next to the library (<see cref="JsonFile"/>).</summary>
internal sealed class ViewStateStore(string path, bool isPersistent)
{
    private readonly JsonFile _file = new(path, isPersistent);

    public LibraryViewState Load()
    {
        try
        {
            return _file.Read() is { } json ? LibraryViewState.FromJson(json) : new LibraryViewState();
        }
        catch (System.Text.Json.JsonException)
        {
            return new LibraryViewState();
        }
    }

    public void Save(LibraryViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _file.Write(state.ToJson());
    }
}
