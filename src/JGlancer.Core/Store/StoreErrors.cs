namespace JGlancer.Core.Store;

/// <summary>Errors raised by <see cref="LibraryStore"/>.</summary>
public class StoreException : Exception
{
    public StoreException()
    {
    }

    public StoreException(string message)
        : base(message)
    {
    }

    public StoreException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>The folder is already a library root.</summary>
public sealed class RootAlreadyExistsException(string path) : StoreException($"already in the library: {path}")
{
    public string Path { get; } = path;
}

/// <summary>The folder lies inside, or contains, an existing root.</summary>
public sealed class RootOverlapsException(string path, string existing) : StoreException($"{path} overlaps {existing}")
{
    public string Path { get; } = path;

    public string Existing { get; } = existing;
}

/// <summary>No root with this id.</summary>
public sealed class RootNotFoundException(long id) : StoreException($"no root {id}")
{
    public long Id { get; } = id;
}
