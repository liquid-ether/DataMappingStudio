using System.Globalization;
using App.Application.Abstractions;
using App.Domain.Data;
using App.Domain.Entities;

namespace App.Application.Catalog;

/// <summary>A data source surfaced for selection (its identity + display name).</summary>
public sealed record DataSourceInfo(Guid Id, string Name);

/// <summary>
/// Read-only queries over the real data model (the <c>data_source</c> and <c>dictionary_entry</c>
/// tables) used to drive the Mapping Studio and lineage from actual catalog data rather than
/// free-text aliases: the list of sources, and each source's dictionary columns.
/// </summary>
public interface ICatalogQuery
{
    /// <summary>All data sources, ordered by name (suitable for an autocomplete picker).</summary>
    IReadOnlyList<DataSourceInfo> DataSources();

    /// <summary>The dictionary column names for a data source (by name), in ordinal order.</summary>
    IReadOnlyList<string> FieldNames(string dataSourceName);

    /// <summary>Every data source's dictionary columns, keyed by source name — one pass over the store.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> FieldsByDataSourceName();
}

public sealed class CatalogQuery(ILocalStore store) : ICatalogQuery
{
    public IReadOnlyList<DataSourceInfo> DataSources() =>
        store.GetAll(TableNames.DataSource)
            .Select(r => new DataSourceInfo(r.Id, r["name"] ?? r.Id.ToString()))
            .Where(d => d.Name.Length > 0)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<string> FieldNames(string dataSourceName)
    {
        Row? source = store.GetAll(TableNames.DataSource).FirstOrDefault(r => r["name"] == dataSourceName);
        if (source is null)
        {
            return [];
        }

        string id = source.Id.ToString();
        return store.GetAll(TableNames.DictionaryEntry)
            .Where(r => r["source_id"] == id)
            .OrderBy(r => Ordinal(r["ordinal"]))
            .Select(r => r["column_name"] ?? string.Empty)
            .Where(c => c.Length > 0)
            .ToList();
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> FieldsByDataSourceName()
    {
        Dictionary<Guid, string> nameById = store.GetAll(TableNames.DataSource)
            .ToDictionary(r => r.Id, r => r["name"] ?? r.Id.ToString());

        Dictionary<string, IReadOnlyList<string>> result = new(StringComparer.Ordinal);
        foreach (IGrouping<string?, Row> group in store.GetAll(TableNames.DictionaryEntry).GroupBy(r => r["source_id"]))
        {
            if (group.Key is null || !Guid.TryParse(group.Key, out Guid sourceId) || !nameById.TryGetValue(sourceId, out string? name))
            {
                continue;
            }

            result[name] = group
                .OrderBy(r => Ordinal(r["ordinal"]))
                .Select(r => r["column_name"] ?? string.Empty)
                .Where(c => c.Length > 0)
                .ToList();
        }

        return result;
    }

    private static int Ordinal(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue;
}
