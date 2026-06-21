using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Local;

/// <summary>
/// Owns the single open connection to the analyst's local SQLite working copy (one DB per analyst,
/// all tables co-located so cross-application mappings work — Architecture §6). Registered as a
/// singleton; tests construct it directly with a temp-file or shared in-memory connection string.
/// </summary>
public sealed class LocalDatabase : IDisposable
{
    public LocalDatabase(string connectionString)
    {
        Connection = new SqliteConnection(connectionString);
        Connection.Open();
        // Durability/consistency pragmas appropriate for a single-user local working copy.
        using SqliteCommand pragma = Connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
    }

    public SqliteConnection Connection { get; }

    public void Dispose() => Connection.Dispose();
}
