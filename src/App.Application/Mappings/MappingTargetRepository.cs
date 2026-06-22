using App.Application.Abstractions;
using App.Application.Lineage;
using App.Domain.Data;
using App.Domain.Entities;

namespace App.Application.Mappings;

/// <summary>
/// Reads and writes the Mapping Studio target/source (alias) structure to the local store
/// (<c>mapping_target</c> + <c>mapping_source</c> tables), so the lineage context is persisted and
/// syncs like everything else. Source field lists are stored comma-separated.
/// </summary>
public interface IMappingTargetRepository
{
    IReadOnlyList<LineageTarget> GetAll();

    /// <summary>Upserts a target and its sources (writing change-log entries).</summary>
    void Save(LineageTarget target, string changedBy);
}

public sealed class MappingTargetRepository(ILocalStore store) : IMappingTargetRepository
{
    public IReadOnlyList<LineageTarget> GetAll()
    {
        List<Row> sourceRows = store.GetAll(TableNames.MappingSource).ToList();

        return store.GetAll(TableNames.MappingTarget)
            .Select(t => t["name"] ?? string.Empty)
            .Where(name => name.Length > 0)
            .Select(name => new LineageTarget(name, sourceRows
                .Where(s => s["target"] == name)
                .OrderBy(s => s["alias"], StringComparer.Ordinal)
                .Select(ToSource)
                .ToList()))
            .ToList();
    }

    public void Save(LineageTarget target, string changedBy)
    {
        string changeSet = Guid.NewGuid().ToString();

        Guid targetId = FindId(TableNames.MappingTarget, r => r["name"] == target.Name) ?? Guid.NewGuid();
        store.Upsert(TableNames.MappingTarget, new Row(TableNames.MappingTarget, targetId) { ["name"] = target.Name }, changeSet, changedBy);

        foreach (LineageSource source in target.Sources)
        {
            Guid sourceId = FindId(TableNames.MappingSource, r => r["target"] == target.Name && r["alias"] == source.Alias) ?? Guid.NewGuid();
            store.Upsert(TableNames.MappingSource, new Row(TableNames.MappingSource, sourceId)
            {
                ["target"] = target.Name,
                ["alias"] = source.Alias,
                ["cls"] = source.Cls,
                ["source_name"] = source.Name,
                ["is_target"] = source.IsTarget ? "true" : "false",
                ["fields"] = string.Join(",", source.Fields),
            }, changeSet, changedBy);
        }
    }

    private Guid? FindId(string table, Func<Row, bool> match)
    {
        Row? row = store.GetAll(table).FirstOrDefault(match);
        return row?.Id;
    }

    private static LineageSource ToSource(Row row)
    {
        bool isTarget = row["is_target"] == "true";
        string fields = row["fields"] ?? string.Empty;
        IReadOnlyList<string> fieldList = fields.Length == 0
            ? []
            : fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new LineageSource(row["alias"] ?? string.Empty, row["cls"] ?? string.Empty, row["source_name"] ?? string.Empty, isTarget, fieldList);
    }
}
