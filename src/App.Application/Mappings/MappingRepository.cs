using App.Application.Abstractions;
using App.Domain.Data;
using App.Domain.Entities;

namespace App.Application.Mappings;

/// <summary>A persisted mapping row (the flat editable shape the Mapping Studio grid works with).</summary>
public sealed record MappingRecord(
    Guid Id,
    string Target,
    string Field,
    MappingKind Kind,
    string Type,
    string Expression,
    bool IsTokenized = false,
    string? Notes = null);

/// <summary>
/// Reads and writes Mapping Studio rows to the local store's <c>mapping</c> table. Every write goes
/// through <see cref="ILocalStore"/>, so each edit produces a change-log entry that the publish/sync
/// flow picks up (this is what makes studio edits syncable, Architecture §6/§8).
/// </summary>
public interface IMappingRepository
{
    IReadOnlyList<MappingRecord> GetAll();

    /// <summary>Inserts or updates a mapping row (writes change-log entries for changed fields).</summary>
    void Save(MappingRecord record, string changedBy);

    /// <summary>Soft-deletes a mapping row (writes a Delete change-log entry).</summary>
    void Delete(Guid id, string changedBy);
}

public sealed class MappingRepository(ILocalStore store) : IMappingRepository
{
    private const string Table = TableNames.Mapping;

    public IReadOnlyList<MappingRecord> GetAll() => store.GetAll(Table).Select(Map).ToList();

    public void Save(MappingRecord record, string changedBy)
    {
        Row row = new(Table, record.Id)
        {
            ["target"] = NullIfEmpty(record.Target),
            ["field"] = NullIfEmpty(record.Field),
            ["kind"] = record.Kind.ToString(),
            ["type"] = NullIfEmpty(record.Type),
            ["expression"] = NullIfEmpty(record.Expression),
            ["is_tokenized"] = record.IsTokenized ? "true" : "false",
            ["notes"] = NullIfEmpty(record.Notes),
        };
        store.Upsert(Table, row, Guid.NewGuid().ToString(), changedBy);
    }

    public void Delete(Guid id, string changedBy) => store.SoftDelete(Table, id, Guid.NewGuid().ToString(), changedBy);

    private static MappingRecord Map(Row row) => new(
        row.Id,
        row["target"] ?? string.Empty,
        row["field"] ?? string.Empty,
        Enum.TryParse(row["kind"], out MappingKind kind) ? kind : MappingKind.Field,
        row["type"] ?? string.Empty,
        row["expression"] ?? string.Empty,
        row["is_tokenized"] == "true",
        row["notes"]);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
