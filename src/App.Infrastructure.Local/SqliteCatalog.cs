using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Infrastructure.Local.Sqlite;
using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Local;

/// <summary>
/// SQLite-backed column catalog. Stored in the <c>column_catalog</c> meta table; folded like any other
/// table (Architecture §4/§6a). <see cref="AddColumn"/> both records the column and applies the
/// physical <c>ALTER TABLE … ADD COLUMN</c> so it appears in the editors with no code change.
/// </summary>
public sealed class SqliteCatalog : ICatalog
{
    private const string Table = "column_catalog";
    private readonly LocalDatabase _db;

    public SqliteCatalog(LocalDatabase db)
    {
        _db = db;
        EnsureTable();
    }

    public IReadOnlyList<ColumnCatalogEntry> GetAll()
        => _db.Locked(() => _db.Connection.Query($"{SelectColumns} FROM {Table} ORDER BY table_name, display_order, column_name", Map));

    public IReadOnlyList<ColumnCatalogEntry> GetForTable(string table)
        => _db.Locked(() => _db.Connection.Query(
            $"{SelectColumns} FROM {Table} WHERE table_name = $t ORDER BY display_order, column_name",
            Map,
            ("$t", table)));

    public IReadOnlyList<string> GetTables()
        => _db.Locked(() => _db.Connection.Query($"SELECT DISTINCT table_name FROM {Table} ORDER BY table_name", static r => r.GetString(0)));

    public void Seed(IEnumerable<ColumnCatalogEntry> entries) => _db.Locked(() =>
    {
        foreach (ColumnCatalogEntry entry in entries)
        {
            Validate(entry);
            Insert(entry, ignoreIfExists: true);
        }
    });

    public void AddColumn(ColumnCatalogEntry entry) => _db.Locked(() =>
    {
        Validate(entry);
        if (entry.IsCore)
        {
            throw new InvalidOperationException("Core columns are managed by the store and cannot be added via AddColumn.");
        }

        // Reject an unsafe identifier up front, before inserting the catalog row — otherwise the entry
        // would persist but the physical ALTER TABLE (which quotes the name) would throw, leaving a
        // phantom column the store can never materialize.
        if (!SqlName.IsValidIdentifier(entry.ColumnName))
        {
            throw new ArgumentException(
                $"Invalid column name '{entry.ColumnName}'. Use letters, digits and underscores, starting with a letter or underscore.",
                nameof(entry));
        }

        Insert(entry, ignoreIfExists: false);

        // Apply the physical column if the table already exists and lacks it.
        SqliteConnection c = _db.Connection;
        if (c.TableExists(entry.TableName) && !c.ColumnNames(entry.TableName).Contains(entry.ColumnName))
        {
            c.Execute($"ALTER TABLE {SqlIdentifier.Quote(entry.TableName)} ADD COLUMN {SqlIdentifier.Quote(entry.ColumnName)} TEXT");
        }
    });

    private void Insert(ColumnCatalogEntry e, bool ignoreIfExists)
    {
        string verb = ignoreIfExists ? "INSERT OR IGNORE" : "INSERT";
        _db.Connection.Execute(
            $"""
            {verb} INTO {Table}
              (table_name, column_name, kind, value_type, max_length, label_en, label_fr,
               is_required, is_core, is_user_added, reference_target, formula, default_value, display_order)
            VALUES ($t, $c, $k, $vt, $max, $len, $lfr, $req, $core, $usr, $ref, $f, $def, $ord)
            """,
            ("$t", e.TableName), ("$c", e.ColumnName), ("$k", (long)e.Kind), ("$vt", (long)e.ValueType),
            ("$max", e.MaxLength is null ? null : (object)(long)e.MaxLength.Value),
            ("$len", e.LabelEn), ("$lfr", e.LabelFr),
            ("$req", e.IsRequired ? 1L : 0L), ("$core", e.IsCore ? 1L : 0L), ("$usr", e.IsUserAdded ? 1L : 0L),
            ("$ref", e.ReferenceTarget), ("$f", e.Formula), ("$def", e.DefaultValue), ("$ord", (long)e.DisplayOrder));
    }

    private static void Validate(ColumnCatalogEntry entry)
    {
        IReadOnlyList<string> errors = entry.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException($"Invalid catalog entry {entry.TableName}.{entry.ColumnName}: {string.Join("; ", errors)}");
        }
    }

    private const string SelectColumns =
        "SELECT table_name, column_name, kind, value_type, max_length, label_en, label_fr, " +
        "is_required, is_core, is_user_added, reference_target, formula, default_value, display_order";

    private static ColumnCatalogEntry Map(SqliteDataReader r) => new()
    {
        TableName = r.GetString(0),
        ColumnName = r.GetString(1),
        Kind = (ColumnKind)r.GetInt64(2),
        ValueType = (CatalogValueType)r.GetInt64(3),
        MaxLength = r.IsDBNull(4) ? null : (int)r.GetInt64(4),
        LabelEn = r.AsString(5),
        LabelFr = r.AsString(6),
        IsRequired = r.GetInt64(7) != 0,
        IsCore = r.GetInt64(8) != 0,
        IsUserAdded = r.GetInt64(9) != 0,
        ReferenceTarget = r.AsString(10),
        Formula = r.AsString(11),
        DefaultValue = r.AsString(12),
        DisplayOrder = (int)r.GetInt64(13),
    };

    private void EnsureTable() => _db.Locked(() => _db.Connection.Execute(
        $"""
        CREATE TABLE IF NOT EXISTS {Table} (
          table_name       TEXT NOT NULL,
          column_name      TEXT NOT NULL,
          kind             INTEGER NOT NULL,
          value_type       INTEGER NOT NULL,
          max_length       INTEGER,
          label_en         TEXT,
          label_fr         TEXT,
          is_required      INTEGER NOT NULL DEFAULT 0,
          is_core          INTEGER NOT NULL DEFAULT 0,
          is_user_added    INTEGER NOT NULL DEFAULT 0,
          reference_target TEXT,
          formula          TEXT,
          default_value    TEXT,
          display_order    INTEGER NOT NULL DEFAULT 0,
          PRIMARY KEY (table_name, column_name)
        )
        """));
}
