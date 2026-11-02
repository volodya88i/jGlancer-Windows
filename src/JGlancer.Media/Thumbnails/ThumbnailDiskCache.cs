using Microsoft.Data.Sqlite;

namespace JGlancer.Media.Thumbnails;

/// <summary>
/// <c>cache.sqlite</c>: rebuildable thumbnails as encoded blobs, capped in size with least-recently-used eviction
/// (macOS ThumbnailDiskCache, same schema). Safe to delete at any time; a damaged file is recreated.
/// </summary>
public sealed class ThumbnailDiskCache : IDisposable
{
    /// <summary>Default size cap (FEATURES §14).</summary>
    public const long DefaultLimitBytes = 2L * 1024 * 1024 * 1024;

    // Insertions between eviction checks.
    private const int EvictionInterval = 64;
    private const long RecencyRefreshMilliseconds = 86_400_000;

    private readonly string _connectionString;
    private readonly SqliteConnection? _memoryAnchor;
    private readonly Lock _writeLock = new();
    private long _limitBytes;
    private int _insertions;

    private ThumbnailDiskCache(string connectionString, SqliteConnection? memoryAnchor, long limitBytes)
    {
        _connectionString = connectionString;
        _memoryAnchor = memoryAnchor;
        _limitBytes = limitBytes;
        Migrate();
    }

    /// <summary>Opens the cache at <paramref name="path"/>; a corrupt file is deleted and recreated (derived data only).</summary>
    public static ThumbnailDiskCache Open(string path, long limitBytes = DefaultLimitBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ConnectionString;
        try
        {
            return new ThumbnailDiskCache(connectionString, null, limitBytes);
        }
        catch (SqliteException)
        {
            ClearPool(connectionString);
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                File.Delete(path + suffix);
            }
            return new ThumbnailDiskCache(connectionString, null, limitBytes);
        }
    }

    /// <summary>An in-memory cache, for tests.</summary>
    public static ThumbnailDiskCache InMemory(long limitBytes = DefaultLimitBytes)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "jgthumbs-" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };
        var anchor = new SqliteConnection(builder.ConnectionString);
        anchor.Open();
        return new ThumbnailDiskCache(builder.ConnectionString, anchor, limitBytes);
    }

    /// <summary>The size cap in bytes (Settings → Library).</summary>
    public long LimitBytes => Interlocked.Read(ref _limitBytes);

    /// <summary>The stored entry: encoded image data, empty data meaning "this asset has no thumbnail", or null.</summary>
    public byte[]? Data(ThumbnailKey key, DateTimeOffset? now = null)
    {
        var time = (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
        byte[]? data = null;
        long lastUsed = 0;
        using (var connection = Connect())
        using (var command = Command(connection, "SELECT data, last_used FROM thumb WHERE asset_id = @a AND mtime = @m AND bucket = @b", key))
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                data = (byte[])reader.GetValue(0);
                lastUsed = reader.GetInt64(1);
            }
        }
        // Recency is refreshed at most once a day per entry, so reads rarely write.
        if (data is not null && time - lastUsed > RecencyRefreshMilliseconds)
        {
            Write(connection =>
            {
                using var command = Command(connection, "UPDATE thumb SET last_used = @t WHERE asset_id = @a AND mtime = @m AND bucket = @b", key);
                command.Parameters.AddWithValue("@t", time);
                command.ExecuteNonQuery();
            });
        }
        return data;
    }

    /// <summary>Stores an encoded thumbnail (or empty data for "none") and evicts old entries when over the cap.</summary>
    public void Store(byte[] data, int width, int height, ThumbnailKey key, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var time = (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
        Write(connection =>
        {
            // An upsert, not INSERT OR REPLACE: REPLACE deletes without firing the byte-counting trigger.
            using var command = Command(connection, """
                INSERT INTO thumb (asset_id, mtime, bucket, width, height, data, last_used) VALUES (@a, @m, @b, @w, @h, @d, @t)
                ON CONFLICT (asset_id, mtime, bucket) DO UPDATE SET
                  width = excluded.width, height = excluded.height, data = excluded.data, last_used = excluded.last_used
                """, key);
            command.Parameters.AddWithValue("@w", width);
            command.Parameters.AddWithValue("@h", height);
            command.Parameters.AddWithValue("@d", data);
            command.Parameters.AddWithValue("@t", time);
            command.ExecuteNonQuery();
        });
        if (Interlocked.Increment(ref _insertions) % EvictionInterval == 0)
        {
            EvictIfNeeded();
        }
    }

    /// <summary>Bytes of encoded thumbnails currently stored.</summary>
    public long UsedBytes()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT bytes FROM usage";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Changes the cap; a smaller one evicts at once.</summary>
    public void SetLimit(long bytes)
    {
        Interlocked.Exchange(ref _limitBytes, bytes);
        EvictIfNeeded();
    }

    /// <summary>Deletes least recently used entries until the cache is at 90 % of its cap.</summary>
    public void EvictIfNeeded()
    {
        var limit = LimitBytes;
        var target = limit * 9 / 10;
        Write(connection =>
        {
            using var used = connection.CreateCommand();
            used.CommandText = "SELECT bytes FROM usage";
            var bytes = Convert.ToInt64(used.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            if (bytes <= limit)
            {
                return;
            }
            var excess = bytes - target;
            var victims = new List<long>();
            using (var oldest = connection.CreateCommand())
            {
                oldest.CommandText = "SELECT rowid, length(data) FROM thumb ORDER BY last_used, rowid";
                using var reader = oldest.ExecuteReader();
                while (excess > 0 && reader.Read())
                {
                    victims.Add(reader.GetInt64(0));
                    excess -= reader.GetInt64(1);
                }
            }
            foreach (var chunk in victims.Chunk(500))
            {
                using var delete = connection.CreateCommand();
                delete.CommandText = $"DELETE FROM thumb WHERE rowid IN ({string.Join(",", chunk)})";
                delete.ExecuteNonQuery();
            }
        });
    }

    /// <summary>Empties the cache ("Clear Cache" in Settings).</summary>
    public void RemoveAll() => Write(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM thumb";
        command.ExecuteNonQuery();
    });

    public void Dispose()
    {
        _memoryAnchor?.Dispose();
        ClearPool(_connectionString);
    }

    /// <summary>Closes this cache's idle pooled connections only (ClearAllPools would hit the library's in use).</summary>
    private static void ClearPool(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        SqliteConnection.ClearPool(connection);
    }

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragmas = connection.CreateCommand();
        pragmas.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL";
        pragmas.ExecuteNonQuery();
        return connection;
    }

    private void Write(Action<SqliteConnection> write)
    {
        lock (_writeLock)
        {
            using var connection = Connect();
            using (var begin = connection.CreateCommand())
            {
                begin.CommandText = "BEGIN IMMEDIATE";
                begin.ExecuteNonQuery();
            }
            try
            {
                write(connection);
                using var commit = connection.CreateCommand();
                commit.CommandText = "COMMIT";
                commit.ExecuteNonQuery();
            }
            catch
            {
                using var rollback = connection.CreateCommand();
                rollback.CommandText = "ROLLBACK";
                rollback.ExecuteNonQuery();
                throw;
            }
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, ThumbnailKey key)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@a", key.AssetId);
        command.Parameters.AddWithValue("@m", key.ModifiedAt);
        command.Parameters.AddWithValue("@b", (int)key.Bucket);
        return command;
    }

    private void Migrate()
    {
        if (_memoryAnchor is null)
        {
            using var connection = Connect();
            using var wal = connection.CreateCommand();
            wal.CommandText = "PRAGMA journal_mode = WAL";
            wal.ExecuteNonQuery();
        }
        Write(connection =>
        {
            using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'thumb'";
            if (Convert.ToInt64(check.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0)
            {
                return;
            }
            using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE thumb (
                  asset_id INTEGER NOT NULL,
                  mtime INTEGER NOT NULL,
                  bucket INTEGER NOT NULL,
                  width INTEGER NOT NULL,
                  height INTEGER NOT NULL,
                  data BLOB NOT NULL,
                  last_used INTEGER NOT NULL,
                  PRIMARY KEY (asset_id, mtime, bucket)
                );
                CREATE INDEX thumb_last_used ON thumb(last_used);
                CREATE TABLE usage (id INTEGER PRIMARY KEY CHECK (id = 1), bytes INTEGER NOT NULL);
                INSERT INTO usage VALUES (1, 0);
                CREATE TRIGGER thumb_added AFTER INSERT ON thumb BEGIN
                  UPDATE usage SET bytes = bytes + length(NEW.data);
                END;
                CREATE TRIGGER thumb_removed AFTER DELETE ON thumb BEGIN
                  UPDATE usage SET bytes = bytes - length(OLD.data);
                END;
                CREATE TRIGGER thumb_replaced AFTER UPDATE OF data ON thumb BEGIN
                  UPDATE usage SET bytes = bytes - length(OLD.data) + length(NEW.data);
                END;
                """;
            create.ExecuteNonQuery();
        });
    }
}
