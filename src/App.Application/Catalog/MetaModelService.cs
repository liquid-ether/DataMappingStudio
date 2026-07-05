using App.Application.Abstractions;
using App.Application.Security;
using App.Application.Sync;
using App.Domain.Catalog;
using App.Domain.Data;

namespace App.Application.Catalog;

/// <summary>
/// The single write path for meta-model changes (used by the admin Model module and the grid's
/// add-column): validates, appends to the shared catalog log FIRST (so the change is durable and
/// team-visible before anything local depends on it — a crash in between self-heals on the next
/// refresh), then applies locally. With no shared folder configured (tests, offline tools) it applies
/// locally only, preserving the old behavior.
/// </summary>
public sealed class MetaModelService(
    ICatalog catalog,
    ITableCatalog tables,
    ILocalStore store,
    ICurrentUser user,
    CatalogSyncService? sync = null,
    ISyncCoordinator? coordinator = null)
{
    /// <summary>Names that can never be user tables (meta tables, sync artifacts, SQLite internals).</summary>
    private static readonly HashSet<string> ReservedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "column_catalog", "table_catalog", "app_config", "local_change_log", "sync_state", "settings", "catalog",
    };

    public void CreateTable(TableCatalogEntry meta, IReadOnlyList<ColumnCatalogEntry> columns)
    {
        RequirePermission();
        ValidateTableName(meta.TableName, mustBeNew: true);
        ThrowIfInvalid(meta.Validate(), meta.TableName);
        if (columns.Count == 0)
        {
            throw new ArgumentException("A new table needs at least one column.", nameof(columns));
        }

        foreach (ColumnCatalogEntry column in columns)
        {
            ValidateColumn(column, expectedTable: meta.TableName);
        }

        if (meta.DisplayColumn is { } display && !columns.Any(c => c.ColumnName == display))
        {
            throw new ArgumentException($"Display column '{display}' is not one of the table's columns.");
        }

        TableCatalogEntry stamped = meta with { IsUserAdded = true };
        List<ColumnCatalogEntry> stampedColumns = columns.Select(c => c with { IsUserAdded = true }).ToList();

        PublishChanges(
        [
            Change(CatalogChangeKind.TableAdded, table: stamped),
            .. stampedColumns.Select(c => Change(CatalogChangeKind.ColumnAdded, column: c)),
        ]);

        tables.UpsertTableMeta(stamped);
        catalog.Seed(stampedColumns);
        store.EnsureSchema();
    }

    public void AddColumn(ColumnCatalogEntry column)
    {
        RequirePermission();
        if (!catalog.GetTables().Contains(column.TableName, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unknown table '{column.TableName}'.");
        }

        ValidateColumn(column, expectedTable: column.TableName);
        ColumnCatalogEntry stamped = column with { IsUserAdded = true };

        PublishChanges([Change(CatalogChangeKind.ColumnAdded, column: stamped)]);
        catalog.AddColumn(stamped); // catalog row + physical ALTER TABLE
    }

    public void UpdateTableMeta(TableCatalogEntry meta)
    {
        RequirePermission();
        ThrowIfInvalid(meta.Validate(), meta.TableName);
        PublishChanges([Change(CatalogChangeKind.TableMetaUpdated, table: meta)]);
        tables.UpsertTableMeta(meta);
    }

    private void PublishChanges(List<CatalogChangeEntry> entries)
        => sync?.Publish(coordinator?.WriterId ?? user.Name, entries);

    private CatalogChangeEntry Change(CatalogChangeKind kind, TableCatalogEntry? table = null, ColumnCatalogEntry? column = null) => new()
    {
        ClientSeq = 0, // assigned by CatalogSyncService.Publish
        ChangedBy = user.Name,
        ChangedAtUtc = DateTimeOffset.UtcNow,
        Kind = kind,
        Table = table,
        Column = column,
    };

    private void ValidateTableName(string tableName, bool mustBeNew)
    {
        if (!SqlName.IsValidIdentifier(tableName))
        {
            throw new ArgumentException($"Invalid table name '{tableName}'. Use letters, digits and underscores, starting with a letter or underscore.");
        }

        if (ReservedTables.Contains(tableName) || tableName.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{tableName}' is a reserved name.");
        }

        if (mustBeNew && catalog.GetTables().Contains(tableName, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Table '{tableName}' already exists.");
        }
    }

    private void ValidateColumn(ColumnCatalogEntry column, string expectedTable)
    {
        if (column.TableName != expectedTable)
        {
            throw new ArgumentException($"Column '{column.ColumnName}' targets table '{column.TableName}', expected '{expectedTable}'.");
        }

        if (!SqlName.IsValidIdentifier(column.ColumnName))
        {
            throw new ArgumentException($"Invalid column name '{column.ColumnName}'.");
        }

        if (column.ColumnName is SyncColumns.Id or SyncColumns.RowVersion or SyncColumns.BaseVersion
            or SyncColumns.ModifiedAt or SyncColumns.ModifiedBy or SyncColumns.IsDeleted)
        {
            throw new ArgumentException($"'{column.ColumnName}' is a reserved core column name.");
        }

        ThrowIfInvalid(column.Validate(), $"{column.TableName}.{column.ColumnName}");

        if (column.Kind == ColumnKind.Reference
            && !catalog.GetTables().Contains(column.ReferenceTarget!, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Reference target table '{column.ReferenceTarget}' does not exist.");
        }
    }

    private void RequirePermission()
    {
        if (!user.HasPermission(Permissions.ModelManage))
        {
            throw new UnauthorizedAccessException("Managing the meta-model requires the Model.Manage permission.");
        }
    }

    private static void ThrowIfInvalid(IReadOnlyList<string> errors, string subject)
    {
        if (errors.Count > 0)
        {
            throw new ArgumentException($"Invalid definition for '{subject}': {string.Join("; ", errors)}");
        }
    }
}
