using System.Globalization;
using App.Domain.Catalog;
using App.Domain.Data;

namespace App.Infrastructure.Remote;

/// <summary>Maps <see cref="ChangeLogEntry"/> records to/from the tabular <see cref="RemoteTable"/> form.</summary>
internal static class ChangeLogSchema
{
    public static readonly IReadOnlyList<RemoteColumn> Columns =
    [
        new("change_id", CatalogValueType.Uuid),
        new("change_set_id", CatalogValueType.Text),
        new("table_name", CatalogValueType.Text),
        new("row_id", CatalogValueType.Uuid),
        new("column_name", CatalogValueType.Text),
        new("old_value", CatalogValueType.Text),
        new("new_value", CatalogValueType.Text),
        new("operation", CatalogValueType.Integer),
        new("changed_by", CatalogValueType.Text),
        new("changed_at", CatalogValueType.Timestamp),
        new("client_seq", CatalogValueType.Integer),
    ];

    public static IReadOnlyList<string?> ToRow(ChangeLogEntry e) =>
    [
        e.ChangeId.ToString(),
        e.ChangeSetId,
        e.Table,
        e.RowId.ToString(),
        e.Column,
        e.OldValue,
        e.NewValue,
        ((long)e.Operation).ToString(CultureInfo.InvariantCulture),
        e.ChangedBy,
        e.ChangedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        e.ClientSeq.ToString(CultureInfo.InvariantCulture),
    ];

    public static ChangeLogEntry FromRow(IReadOnlyList<string?> row) => new()
    {
        ChangeId = Guid.Parse(row[0]!),
        ChangeSetId = row[1] ?? string.Empty,
        Table = row[2] ?? string.Empty,
        RowId = Guid.Parse(row[3]!),
        Column = row[4] ?? string.Empty,
        OldValue = row[5],
        NewValue = row[6],
        Operation = (ChangeOperation)long.Parse(row[7]!, CultureInfo.InvariantCulture),
        ChangedBy = row[8] ?? string.Empty,
        ChangedAtUtc = DateTimeOffset.Parse(row[9]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        ClientSeq = long.Parse(row[10]!, CultureInfo.InvariantCulture),
    };
}
