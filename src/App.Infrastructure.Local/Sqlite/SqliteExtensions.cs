using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Local.Sqlite;

/// <summary>Minimal helpers over <see cref="SqliteConnection"/> — we use a thin generic store, not EF Core.</summary>
internal static class SqliteExtensions
{
    public static int Execute(this SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        return command.ExecuteNonQuery();
    }

    public static object? Scalar(this SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        object? result = command.ExecuteScalar();
        return result is DBNull ? null : result;
    }

    /// <summary>Reads rows, projecting each via <paramref name="project"/>.</summary>
    public static List<T> Query<T>(
        this SqliteConnection connection,
        string sql,
        Func<SqliteDataReader, T> project,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);

        List<T> results = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(project(reader));
        }

        return results;
    }

    /// <summary>Column names currently present on a table (via <c>PRAGMA table_info</c>).</summary>
    public static HashSet<string> ColumnNames(this SqliteConnection connection, string table)
    {
        List<string> names = connection.Query(
            $"PRAGMA table_info({SqlIdentifier.Quote(table)})",
            static r => r.GetString(r.GetOrdinal("name")));
        return new HashSet<string>(names, StringComparer.Ordinal);
    }

    public static bool TableExists(this SqliteConnection connection, string table)
        => connection.Scalar(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $n",
            ("$n", table)) is not null;

    public static string? AsString(this SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static void AddParameters(SqliteCommand command, (string Name, object? Value)[] parameters)
    {
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}
