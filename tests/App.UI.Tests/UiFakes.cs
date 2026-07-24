using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Domain.Data;

namespace App.UI.Tests;

/// <summary>In-memory catalog for component tests (mutable so AddColumn is observable).</summary>
public sealed class FakeCatalog(IEnumerable<ColumnCatalogEntry> seed) : ICatalog
{
    private readonly List<ColumnCatalogEntry> _entries = [.. seed];

    public IReadOnlyList<ColumnCatalogEntry> GetAll() => _entries;

    public IReadOnlyList<ColumnCatalogEntry> GetForTable(string table) => _entries.Where(e => e.TableName == table).ToList();

    public IReadOnlyList<string> GetTables() => _entries.Select(e => e.TableName).Distinct().ToList();

    public void Seed(IEnumerable<ColumnCatalogEntry> entries) => _entries.AddRange(entries);

    public void AddColumn(ColumnCatalogEntry entry) => _entries.Add(entry);

    public void UpdateColumnMeta(ColumnCatalogEntry entry)
    {
        int i = _entries.FindIndex(e => e.TableName == entry.TableName && e.ColumnName == entry.ColumnName);
        if (i >= 0)
        {
            _entries[i] = _entries[i] with { LabelEn = entry.LabelEn, LabelFr = entry.LabelFr };
        }
    }
}

/// <summary>In-memory table catalog for component tests.</summary>
public sealed class FakeTableCatalog(IEnumerable<TableCatalogEntry>? seed = null) : ITableCatalog
{
    private readonly Dictionary<string, TableCatalogEntry> _entries =
        (seed ?? []).ToDictionary(e => e.TableName, StringComparer.Ordinal);

    public IReadOnlyList<TableCatalogEntry> GetTableMeta() => [.. _entries.Values.OrderBy(e => e.NavOrder)];

    public TableCatalogEntry? GetTableMeta(string table) => _entries.GetValueOrDefault(table);

    public void UpsertTableMeta(TableCatalogEntry entry) => _entries[entry.TableName] = entry;

    public void SeedTableMeta(IEnumerable<TableCatalogEntry> entries)
    {
        foreach (TableCatalogEntry entry in entries)
        {
            _entries.TryAdd(entry.TableName, entry);
        }
    }
}

/// <summary>In-memory catalog-change remote for tests exercising the catalog sync loop.</summary>
public sealed class FakeCatalogRemote : ICatalogRemote
{
    private readonly Dictionary<string, List<CatalogChangeEntry>> _logs = new(StringComparer.Ordinal);
    private int _version;

    public string CatalogVersion() => _version.ToString();

    public IReadOnlyList<CatalogChangeEntry> ReadAll() => _logs.Values.SelectMany(e => e).ToList();

    public IReadOnlyList<CatalogChangeEntry> ReadWriter(string writerId) => _logs.GetValueOrDefault(writerId) ?? [];

    public void Append(string writerId, IReadOnlyList<CatalogChangeEntry> entries)
    {
        if (!_logs.TryGetValue(writerId, out List<CatalogChangeEntry>? log))
        {
            log = _logs[writerId] = [];
        }

        log.AddRange(entries);
        _version++;
    }
}

/// <summary>In-memory local store for component tests.</summary>
public sealed class FakeLocalStore : ILocalStore
{
    private readonly Dictionary<string, Dictionary<Guid, Row>> _tables = new(StringComparer.Ordinal);

    public void EnsureSchema()
    {
    }

    public Row? GetById(string table, Guid id)
        => _tables.TryGetValue(table, out Dictionary<Guid, Row>? rows) && rows.TryGetValue(id, out Row? row) ? row : null;

    public IReadOnlyList<Row> GetAll(string table, bool includeDeleted = false)
        => _tables.TryGetValue(table, out Dictionary<Guid, Row>? rows)
            ? rows.Values.Where(r => includeDeleted || !r.IsDeleted).ToList()
            : [];

    public IReadOnlyList<ChangeLogEntry> Upsert(string table, Row row, string changeSetId, string changedBy, ChangeOperation? operation = null)
    {
        if (!_tables.TryGetValue(table, out Dictionary<Guid, Row>? rows))
        {
            rows = _tables[table] = [];
        }

        rows[row.Id] = row;
        return [];
    }

    public IReadOnlyList<ChangeLogEntry> SoftDelete(string table, Guid id, string changeSetId, string changedBy)
    {
        if (_tables.TryGetValue(table, out Dictionary<Guid, Row>? rows) && rows.TryGetValue(id, out Row? row))
        {
            row.IsDeleted = true;
        }

        return [];
    }

    public void AdoptCanonical(string table, Guid rowId, IReadOnlyDictionary<string, string?> values)
    {
        if (!_tables.TryGetValue(table, out Dictionary<Guid, Row>? rows))
        {
            rows = _tables[table] = [];
        }

        if (!rows.TryGetValue(rowId, out Row? row))
        {
            row = rows[rowId] = new Row(table, rowId);
        }

        foreach ((string column, string? value) in values)
        {
            row[column] = value;
        }
    }
}
