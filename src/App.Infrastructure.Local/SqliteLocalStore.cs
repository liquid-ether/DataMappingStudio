using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Domain.Data;
using App.Domain.Values;
using App.Infrastructure.Local.Sqlite;
using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Local;

/// <summary>
/// The generic, metadata-driven local store. Reads the catalog to create tables and build
/// parameterized SQL; every write diffs against the stored row and emits one change-log entry per
/// changed field (Architecture §4, §8). No EF Core, no fixed POCOs — works for any catalog shape.
/// </summary>
public sealed class SqliteLocalStore : ILocalStore
{
    private readonly LocalDatabase _db;
    private readonly ICatalog _catalog;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public SqliteLocalStore(LocalDatabase db, ICatalog catalog, IAuditLog audit, IClock clock)
    {
        _db = db;
        _catalog = catalog;
        _audit = audit;
        _clock = clock;
    }

    public void EnsureSchema() => _db.Locked(() =>
    {
        SqliteConnection c = _db.Connection;
        foreach (string table in _catalog.GetTables())
        {
            IReadOnlyList<string> dataColumns = DataColumns(table);

            if (!c.TableExists(table))
            {
                string cols = string.Join(", ", dataColumns.Select(col => $"{SqlIdentifier.Quote(col)} TEXT"));
                string columnsClause = cols.Length > 0 ? ", " + cols : string.Empty;
                c.Execute($"CREATE TABLE {SqlIdentifier.Quote(table)} ({LocalStoreSchema.CoreColumnsDdl}{columnsClause})");
            }
            else
            {
                HashSet<string> existing = c.ColumnNames(table);
                foreach (string col in dataColumns.Where(col => !existing.Contains(col)))
                {
                    c.Execute($"ALTER TABLE {SqlIdentifier.Quote(table)} ADD COLUMN {SqlIdentifier.Quote(col)} TEXT");
                }
            }
        }
    });

    public Row? GetById(string table, Guid id) => _db.Locked(() =>
    {
        IReadOnlyList<string> dataColumns = PhysicalDataColumns(table);
        string select = BuildSelect(table, dataColumns);
        List<Row> rows = _db.Connection.Query($"{select} WHERE {Q(LocalStoreSchema.Id)} = $id", r => MapRow(table, dataColumns, r), ("$id", id.ToString()));
        return rows.Count == 0 ? null : rows[0];
    });

    public IReadOnlyList<Row> GetAll(string table, bool includeDeleted = false) => _db.Locked(() =>
    {
        IReadOnlyList<string> dataColumns = PhysicalDataColumns(table);
        string select = BuildSelect(table, dataColumns);
        string where = includeDeleted ? string.Empty : $" WHERE {Q(LocalStoreSchema.IsDeleted)} = 0";
        return _db.Connection.Query($"{select}{where} ORDER BY {Q(LocalStoreSchema.Id)}", r => MapRow(table, dataColumns, r));
    });

    public IReadOnlyList<ChangeLogEntry> Upsert(string table, Row row, string changeSetId, string changedBy, ChangeOperation? operation = null) => _db.Locked<IReadOnlyList<ChangeLogEntry>>(() =>
    {
        Dictionary<string, ColumnCatalogEntry> catalog = _catalog.GetForTable(table)
            .Where(e => !e.IsCore && !LocalStoreSchema.CoreColumns.Contains(e.ColumnName))
            .ToDictionary(e => e.ColumnName, StringComparer.Ordinal);

        // Normalize provided values per catalog type so storage and diffs are canonical.
        Dictionary<string, string?> incoming = new(StringComparer.Ordinal);
        foreach (string col in row.Columns)
        {
            if (!catalog.TryGetValue(col, out ColumnCatalogEntry? entry))
            {
                throw new ArgumentException($"Column '{col}' is not in the catalog for table '{table}'.");
            }

            incoming[col] = ValueNormalizer.Normalize(entry.ValueType, row[col]);
        }

        Row? existing = GetById(table, row.Id);
        bool isNew = existing is null;
        DateTimeOffset now = _clock.UtcNow;

        List<(string Column, string? Old, string? New)> changes = [];
        foreach ((string col, string? newVal) in incoming)
        {
            string? oldVal = existing?[col];
            if (!string.Equals(oldVal, newVal, StringComparison.Ordinal))
            {
                changes.Add((col, oldVal, newVal));
            }
        }

        if (!isNew && changes.Count == 0)
        {
            return [];
        }

        long newVersion = (existing?.RowVersion ?? 0) + 1;
        SqliteConnection c = _db.Connection;

        if (isNew)
        {
            List<string> cols = [LocalStoreSchema.Id, LocalStoreSchema.RowVersion, LocalStoreSchema.ModifiedAt, LocalStoreSchema.ModifiedBy];
            List<(string, object?)> ps =
            [
                ("$id", row.Id.ToString()),
                ("$rv", newVersion),
                ("$ma", now.ToString("O")),
                ("$mb", changedBy),
            ];
            int i = 0;
            foreach ((string col, string? newVal) in incoming)
            {
                cols.Add(col);
                ps.Add(($"$d{i}", newVal));
                i++;
            }

            string colList = string.Join(", ", cols.Select(Q));
            string valList = string.Join(", ", new[] { "$id", "$rv", "$ma", "$mb" }.Concat(Enumerable.Range(0, i).Select(n => $"$d{n}")));
            c.Execute($"INSERT INTO {Q(table)} ({colList}) VALUES ({valList})", [.. ps]);
        }
        else
        {
            List<string> setClauses = [$"{Q(LocalStoreSchema.RowVersion)} = $rv", $"{Q(LocalStoreSchema.ModifiedAt)} = $ma", $"{Q(LocalStoreSchema.ModifiedBy)} = $mb"];
            List<(string, object?)> ps =
            [
                ("$rv", newVersion),
                ("$ma", now.ToString("O")),
                ("$mb", changedBy),
                ("$id", row.Id.ToString()),
            ];
            int i = 0;
            foreach ((string col, _, string? newVal) in changes)
            {
                setClauses.Add($"{Q(col)} = $d{i}");
                ps.Add(($"$d{i}", newVal));
                i++;
            }

            c.Execute($"UPDATE {Q(table)} SET {string.Join(", ", setClauses)} WHERE {Q(LocalStoreSchema.Id)} = $id", [.. ps]);
        }

        ChangeOperation op = operation ?? (isNew ? ChangeOperation.Insert : ChangeOperation.Update);
        IEnumerable<ChangeLogEntry> entries = changes.Select(ch => new ChangeLogEntry
        {
            ChangeId = Guid.NewGuid(),
            ChangeSetId = changeSetId,
            Table = table,
            RowId = row.Id,
            Column = ch.Column,
            OldValue = ch.Old,
            NewValue = ch.New,
            Operation = op,
            ChangedBy = changedBy,
            ChangedAtUtc = now,
        });

        IReadOnlyList<ChangeLogEntry> written = _audit.Append(entries);
        row.RowVersion = newVersion;
        return written;
    });

    public IReadOnlyList<ChangeLogEntry> SoftDelete(string table, Guid id, string changeSetId, string changedBy) => _db.Locked<IReadOnlyList<ChangeLogEntry>>(() =>
    {
        Row? existing = GetById(table, id);
        if (existing is null || existing.IsDeleted)
        {
            return [];
        }

        DateTimeOffset now = _clock.UtcNow;
        long newVersion = existing.RowVersion + 1;
        _db.Connection.Execute(
            $"UPDATE {Q(table)} SET {Q(LocalStoreSchema.IsDeleted)} = 1, {Q(LocalStoreSchema.RowVersion)} = $rv, {Q(LocalStoreSchema.ModifiedAt)} = $ma, {Q(LocalStoreSchema.ModifiedBy)} = $mb WHERE {Q(LocalStoreSchema.Id)} = $id",
            ("$rv", newVersion), ("$ma", now.ToString("O")), ("$mb", changedBy), ("$id", id.ToString()));

        return _audit.Append(
        [
            new ChangeLogEntry
            {
                ChangeId = Guid.NewGuid(),
                ChangeSetId = changeSetId,
                Table = table,
                RowId = id,
                Column = LocalStoreSchema.IsDeleted,
                OldValue = "false",
                NewValue = "true",
                Operation = ChangeOperation.Delete,
                ChangedBy = changedBy,
                ChangedAtUtc = now,
            },
        ]);
    });

    public void AdoptCanonical(string table, Guid rowId, IReadOnlyDictionary<string, string?> values) => _db.Locked(() =>
    {
        if (values.Count == 0)
        {
            return;
        }

        SqliteConnection c = _db.Connection;
        bool exists = c.Scalar($"SELECT 1 FROM {Q(table)} WHERE {Q(LocalStoreSchema.Id)} = $id", ("$id", rowId.ToString())) is not null;

        if (exists)
        {
            List<string> sets = [];
            List<(string, object?)> ps = [("$id", rowId.ToString())];
            int i = 0;
            foreach ((string col, string? val) in values)
            {
                sets.Add($"{Q(col)} = $v{i}");
                ps.Add(($"$v{i}", val));
                i++;
            }

            // base_version follows row_version: the local copy is now in sync for these cells.
            c.Execute($"UPDATE {Q(table)} SET {string.Join(", ", sets)}, {Q(LocalStoreSchema.BaseVersion)} = {Q(LocalStoreSchema.RowVersion)} WHERE {Q(LocalStoreSchema.Id)} = $id", [.. ps]);
        }
        else
        {
            List<string> cols = [LocalStoreSchema.Id];
            List<string> vals = ["$id"];
            List<(string, object?)> ps = [("$id", rowId.ToString())];
            int i = 0;
            foreach ((string col, string? val) in values)
            {
                cols.Add(col);
                vals.Add($"$v{i}");
                ps.Add(($"$v{i}", val));
                i++;
            }

            c.Execute($"INSERT INTO {Q(table)} ({string.Join(", ", cols.Select(Q))}) VALUES ({string.Join(", ", vals)})", [.. ps]);
        }
    });

    private IReadOnlyList<string> DataColumns(string table)
        => _catalog.GetForTable(table)
            .Where(e => !e.IsCore && !LocalStoreSchema.CoreColumns.Contains(e.ColumnName))
            .Select(e => e.ColumnName)
            .ToList();

    private IReadOnlyList<string> PhysicalDataColumns(string table)
        => _db.Connection.ColumnNames(table).Where(c => !LocalStoreSchema.CoreColumns.Contains(c)).ToList();

    private static string BuildSelect(string table, IReadOnlyList<string> dataColumns)
    {
        IEnumerable<string> all = new[] { LocalStoreSchema.Id, LocalStoreSchema.RowVersion, LocalStoreSchema.IsDeleted }.Concat(dataColumns);
        return $"SELECT {string.Join(", ", all.Select(Q))} FROM {Q(table)}";
    }

    private static Row MapRow(string table, IReadOnlyList<string> dataColumns, SqliteDataReader r)
    {
        Row row = new(table, Guid.Parse(r.GetString(0)))
        {
            RowVersion = r.GetInt64(1),
            IsDeleted = r.GetInt64(2) != 0,
        };

        for (int i = 0; i < dataColumns.Count; i++)
        {
            row[dataColumns[i]] = r.AsString(3 + i);
        }

        return row;
    }

    private static string Q(string identifier) => SqlIdentifier.Quote(identifier);
}
