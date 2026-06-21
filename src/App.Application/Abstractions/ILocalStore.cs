using App.Domain.Data;

namespace App.Application.Abstractions;

/// <summary>
/// The generic, metadata-driven local store over the single SQLite working copy. Reads the catalog
/// to build parameterized SQL; rows are catalog-shaped <see cref="Row"/> objects. Every write produces
/// per-field change-log entries via <see cref="IAuditLog"/> (Architecture §4, §6, §8).
/// </summary>
public interface ILocalStore
{
    /// <summary>Creates any missing tables/columns described by the catalog.</summary>
    void EnsureSchema();

    Row? GetById(string table, Guid id);

    IReadOnlyList<Row> GetAll(string table, bool includeDeleted = false);

    /// <summary>
    /// Inserts or updates a row, writing one change-log entry per changed field under
    /// <paramref name="changeSetId"/>. Returns the entries written (empty when nothing changed).
    /// </summary>
    IReadOnlyList<ChangeLogEntry> Upsert(string table, Row row, string changeSetId, string changedBy);

    /// <summary>Soft-deletes a row (sets is_deleted), recording a Delete change-log entry.</summary>
    IReadOnlyList<ChangeLogEntry> SoftDelete(string table, Guid id, string changeSetId, string changedBy);
}
