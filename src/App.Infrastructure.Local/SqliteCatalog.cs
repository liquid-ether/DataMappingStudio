using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Infrastructure.Local.Sqlite;
using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Local;

/// <summary>
/// SQLite-backed column catalog. Stored in the <c>column_catalog</c> meta table; folded like any other
/// table (Architecture §4/§6a). <see cref="AddColumn"/> both records the column and applies the
/// physical <c>ALTER TABLE … ADD COLUMN</c> so it appears in the editors with no code change. Also
/// implements <see cref="ITableCatalog"/> over the sibling <c>table_catalog</c> meta table (labels,
/// navigation placement, reference display column).
/// </summary>
public sealed class SqliteCatalog : ICatalog, ITableCatalog
{
    private const string Table = "column_catalog";
    private const string MetaTable = "table_catalog";
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

    // ---------------- ITableCatalog (table_catalog) ----------------

    public IReadOnlyList<TableCatalogEntry> GetTableMeta()
        => _db.Locked(() => _db.Connection.Query(
            $"{SelectMeta} FROM {MetaTable} ORDER BY nav_order, table_name", MapMeta));

    public TableCatalogEntry? GetTableMeta(string table)
        => _db.Locked(() => _db.Connection.Query(
            $"{SelectMeta} FROM {MetaTable} WHERE table_name = $t", MapMeta, ("$t", table)).FirstOrDefault());

    public void UpsertTableMeta(TableCatalogEntry entry) => _db.Locked(() =>
    {
        ValidateMeta(entry);
        WriteMeta(entry, upsert: true);
    });

    public void SeedTableMeta(IEnumerable<TableCatalogEntry> entries) => _db.Locked(() =>
    {
        foreach (TableCatalogEntry entry in entries)
        {
            ValidateMeta(entry);
            WriteMeta(entry, upsert: false); // INSERT OR IGNORE: seeding never overwrites runtime edits
        }
    });

    private void WriteMeta(TableCatalogEntry e, bool upsert)
    {
        string sql = upsert
            ? $"""
              INSERT INTO {MetaTable} (table_name, label_en, label_fr, nav_visible, nav_order, display_column, is_user_added)
              VALUES ($t, $len, $lfr, $nav, $ord, $disp, $usr)
              ON CONFLICT(table_name) DO UPDATE SET
                label_en = $len, label_fr = $lfr, nav_visible = $nav, nav_order = $ord,
                display_column = $disp, is_user_added = $usr
              """
            : $"""
              INSERT OR IGNORE INTO {MetaTable} (table_name, label_en, label_fr, nav_visible, nav_order, display_column, is_user_added)
              VALUES ($t, $len, $lfr, $nav, $ord, $disp, $usr)
              """;
        _db.Connection.Execute(sql,
            ("$t", e.TableName), ("$len", e.LabelEn), ("$lfr", e.LabelFr),
            ("$nav", e.NavVisible ? 1L : 0L), ("$ord", (long)e.NavOrder),
            ("$disp", e.DisplayColumn), ("$usr", e.IsUserAdded ? 1L : 0L));
    }

    private static void ValidateMeta(TableCatalogEntry entry)
    {
        IReadOnlyList<string> errors = entry.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException($"Invalid table metadata '{entry.TableName}': {string.Join("; ", errors)}");
        }
    }

    private const string SelectMeta =
        "SELECT table_name, label_en, label_fr, nav_visible, nav_order, display_column, is_user_added";

    private static TableCatalogEntry MapMeta(SqliteDataReader r) => new()
    {
        TableName = r.GetString(0),
        LabelEn = r.AsString(1),
        LabelFr = r.AsString(2),
        NavVisible = r.GetInt64(3) != 0,
        NavOrder = (int)r.GetInt64(4),
        DisplayColumn = r.AsString(5),
        IsUserAdded = r.GetInt64(6) != 0,
    };

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

    private void EnsureTable() => _db.Locked(() =>
    {
        _db.Connection.Execute(
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
            """);
        _db.Connection.Execute(
            $"""
            CREATE TABLE IF NOT EXISTS {MetaTable} (
              table_name     TEXT PRIMARY KEY,
              label_en       TEXT,
              label_fr       TEXT,
              nav_visible    INTEGER NOT NULL DEFAULT 0,
              nav_order      INTEGER NOT NULL DEFAULT 0,
              display_column TEXT,
              is_user_added  INTEGER NOT NULL DEFAULT 0
            )
            """);
        return true;
    });
}
