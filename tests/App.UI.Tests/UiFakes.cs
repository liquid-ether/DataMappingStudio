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

    public IReadOnlyList<ChangeLogEntry> Upsert(string table, Row row, string changeSetId, string changedBy)
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
}
