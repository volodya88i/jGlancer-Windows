using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

/// <summary>Small helpers over Microsoft.Data.Sqlite commands with named parameters (<c>@name</c>).</summary>
internal static class Sql
{
    public static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command;
    }

    public static int Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        return command.ExecuteNonQuery();
    }

    public static object? Scalar(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    public static long? Long(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters) =>
        Scalar(connection, sql, parameters) is { } value ? Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) : null;

    public static List<T> Rows<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            rows.Add(map(reader));
        }
        return rows;
    }

    /// <summary><c>@p0, @p1, …</c> for an IN list of <paramref name="values"/>, with the parameters to bind.</summary>
    public static (string List, (string, object?)[] Parameters) InList<T>(IReadOnlyList<T> values, string prefix = "p")
    {
        var parameters = new (string, object?)[values.Count];
        var names = new string[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            names[i] = $"@{prefix}{i}";
            parameters[i] = (names[i], values[i]);
        }
        return (string.Join(",", names), parameters);
    }

    public static long? NullableLong(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    public static int? NullableInt(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static double? NullableDouble(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    }

    public static string? NullableString(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static byte[]? NullableBlob(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : (byte[])reader.GetValue(ordinal);
    }

    public static IEnumerable<IReadOnlyList<T>> Chunks<T>(IReadOnlyList<T> values, int size = 500)
    {
        for (var start = 0; start < values.Count; start += size)
        {
            yield return values.Skip(start).Take(size).ToList();
        }
    }
}
