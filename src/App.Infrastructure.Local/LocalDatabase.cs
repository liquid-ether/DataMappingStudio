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

    /// <summary>
    /// Serializes access to <see cref="Connection"/>. A single <see cref="SqliteConnection"/> is shared
    /// process-wide (one local working copy), but it is not safe to run commands on it from multiple
    /// threads at once — the web host has many Blazor circuits plus the 30-second auto-refresh timer.
    /// Every store/catalog/audit operation takes this lock; it is a <see cref="Monitor"/> (re-entrant),
    /// so nested calls on the same thread (e.g. Upsert → Append) are fine. Operations are short, so a
    /// single coarse lock is sufficient for the ~10-user workload.
    /// </summary>
    public object Gate { get; } = new();

    /// <summary>Runs <paramref name="op"/> under <see cref="Gate"/> (one whole store operation = one lock).</summary>
    public T Locked<T>(Func<T> op)
    {
        lock (Gate)
        {
            return op();
        }
    }

    /// <inheritdoc cref="Locked{T}"/>
    public void Locked(Action op)
    {
        lock (Gate)
        {
            op();
        }
    }

    public void Dispose() => Connection.Dispose();
}
