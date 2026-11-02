using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

/// <summary>
/// The library database (<c>library.sqlite</c>): roots, folders, assets (macOS JGStore, ARCHITECTURE §4).
/// One write transaction at a time; reads run concurrently on their own connections (WAL). Public methods are
/// asynchronous and run on the thread pool, so the UI thread never waits for the database.
/// </summary>
public sealed partial class LibraryStore : IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    // Keeps a shared in-memory database alive between connections.
    private readonly SqliteConnection? _memoryAnchor;
    private bool _disposed;

    private LibraryStore(string connectionString, SqliteConnection? memoryAnchor)
    {
        _connectionString = connectionString;
        _memoryAnchor = memoryAnchor;
        Migrate();
    }

    /// <summary>Raised after every committed write transaction (on the writing thread).</summary>
    public event EventHandler? Changed;

    /// <summary>The database file, or null for an in-memory store.</summary>
    public string? FilePath { get; private init; }

    /// <summary>Opens (creating if needed) the database at <paramref name="path"/> in WAL mode and migrates it.</summary>
    public static LibraryStore Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var connectionString = ConnectionString(path);
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            Sql.Execute(connection, "PRAGMA journal_mode = WAL");
        }
        return new LibraryStore(connectionString, memoryAnchor: null) { FilePath = path };
    }

    private static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = true }.ConnectionString;

    /// <summary>
    /// Closes the idle pooled connections of one database, so its files can be renamed or deleted. Never
    /// <c>ClearAllPools</c>: that tears down other databases' connections while they are in use.
    /// </summary>
    internal static void ClearPool(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        SqliteConnection.ClearPool(connection);
    }

    /// <summary>A private in-memory database, for tests.</summary>
    public static LibraryStore InMemory()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "jglancer-" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };
        var anchor = new SqliteConnection(builder.ConnectionString);
        anchor.Open();
        return new LibraryStore(builder.ConnectionString, anchor);
    }

    /// <summary>Names of the applied migrations, oldest first.</summary>
    public Task<IReadOnlyList<string>> AppliedMigrationsAsync() => ReadAsync<IReadOnlyList<string>>(connection =>
        Sql.Rows(connection, "SELECT identifier FROM schema_migrations ORDER BY rowid", r => r.GetString(0)));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _memoryAnchor?.Dispose();
        // Pooled connections would keep the files open (and block renames or deletes).
        ClearPool(_connectionString);
        _writeLock.Dispose();
    }

    internal SqliteConnection Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Sql.Execute(connection, "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL");
        return connection;
    }

    /// <summary>Runs <paramref name="read"/> on its own connection on the thread pool.</summary>
    internal Task<T> ReadAsync<T>(Func<SqliteConnection, T> read) => Task.Run(() => Read(read));

    internal T Read<T>(Func<SqliteConnection, T> read)
    {
        using var connection = Connect();
        return read(connection);
    }

    /// <summary>Runs <paramref name="write"/> in one transaction, one writer at a time, on the thread pool.</summary>
    internal Task<T> WriteAsync<T>(Func<SqliteConnection, T> write) => Task.Run(() => Write(write));

    internal Task WriteAsync(Action<SqliteConnection> write) => Task.Run(() => Write(connection =>
    {
        write(connection);
        return true;
    }));

    internal T Write<T>(Func<SqliteConnection, T> write)
    {
        _writeLock.Wait();
        try
        {
            T result;
            using (var connection = Connect())
            {
                // Plain SQL transactions: commands then need no transaction object of their own.
                Sql.Execute(connection, "BEGIN IMMEDIATE");
                try
                {
                    result = write(connection);
                    Sql.Execute(connection, "COMMIT");
                }
                catch
                {
                    Sql.Execute(connection, "ROLLBACK");
                    throw;
                }
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return result;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void Migrate()
    {
        Write(connection =>
        {
            Sql.Execute(connection, "CREATE TABLE IF NOT EXISTS schema_migrations (identifier TEXT NOT NULL PRIMARY KEY)");
            var applied = Sql.Rows(connection, "SELECT identifier FROM schema_migrations", r => r.GetString(0)).ToHashSet();
            foreach (var (id, sql) in LibrarySchema.Migrations)
            {
                if (applied.Contains(id))
                {
                    continue;
                }
                Sql.Execute(connection, sql);
                Sql.Execute(connection, "INSERT INTO schema_migrations (identifier) VALUES (@id)", ("@id", id));
            }
            return true;
        });
    }
}
