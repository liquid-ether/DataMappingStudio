using System.Globalization;
using App.Application.Abstractions;
using App.Domain.Data;
using App.Infrastructure.Local.Sqlite;
using Microsoft.Data.Sqlite;

namespace App.Infrastructure.Local;

/// <summary>
/// The local append-only change log (= audit trail, Architecture §8). Each appended entry is assigned
/// a monotonic per-writer <see cref="ChangeLogEntry.ClientSeq"/> via an autoincrement key — the stable
/// ordering the remote fold relies on.
/// </summary>
public sealed class SqliteAuditLog : IAuditLog
{
    private const string Table = "local_change_log";
    private readonly LocalDatabase _db;

    public SqliteAuditLog(LocalDatabase db)
    {
        _db = db;
        EnsureTable();
    }

    public IReadOnlyList<ChangeLogEntry> Append(IEnumerable<ChangeLogEntry> entries) => _db.Locked(() =>
    {
        List<ChangeLogEntry> written = [];
        SqliteConnection c = _db.Connection;

        foreach (ChangeLogEntry e in entries)
        {
            c.Execute(
                $"""
                INSERT INTO {Table}
                  (change_id, change_set_id, table_name, row_id, column_name, old_value, new_value, operation, changed_by, changed_at)
                VALUES ($cid, $csid, $t, $rid, $col, $old, $new, $op, $by, $at)
                """,
                ("$cid", e.ChangeId == Guid.Empty ? Guid.NewGuid().ToString() : e.ChangeId.ToString()),
                ("$csid", e.ChangeSetId),
                ("$t", e.Table),
                ("$rid", e.RowId.ToString()),
                ("$col", e.Column),
                ("$old", e.OldValue),
                ("$new", e.NewValue),
                ("$op", (long)e.Operation),
                ("$by", e.ChangedBy),
                ("$at", e.ChangedAtUtc.ToString("O", CultureInfo.InvariantCulture)));

            long seq = Convert.ToInt64(c.Scalar("SELECT last_insert_rowid()"), CultureInfo.InvariantCulture);
            written.Add(e with { ClientSeq = seq });
        }

        return written;
    });

    public IReadOnlyList<ChangeLogEntry> Query(string? table = null, Guid? rowId = null) => _db.Locked(() =>
    {
        string where = "WHERE 1 = 1";
        List<(string, object?)> ps = [];
        if (table is not null)
        {
            where += " AND table_name = $t";
            ps.Add(("$t", table));
        }

        if (rowId is not null)
        {
            where += " AND row_id = $rid";
            ps.Add(("$rid", rowId.Value.ToString()));
        }

        return _db.Connection.Query(
            $"SELECT client_seq, change_id, change_set_id, table_name, row_id, column_name, old_value, new_value, operation, changed_by, changed_at FROM {Table} {where} ORDER BY client_seq",
            Map,
            [.. ps]);
    });

    public IReadOnlyList<ChangeLogEntry> Pending(long afterClientSeq)
        => _db.Locked(() => _db.Connection.Query(
            $"SELECT client_seq, change_id, change_set_id, table_name, row_id, column_name, old_value, new_value, operation, changed_by, changed_at FROM {Table} WHERE client_seq > $s ORDER BY client_seq",
            Map,
            ("$s", afterClientSeq)));

    private static ChangeLogEntry Map(SqliteDataReader r) => new()
    {
        ClientSeq = r.GetInt64(0),
        ChangeId = Guid.Parse(r.GetString(1)),
        ChangeSetId = r.GetString(2),
        Table = r.GetString(3),
        RowId = Guid.Parse(r.GetString(4)),
        Column = r.GetString(5),
        OldValue = r.AsString(6),
        NewValue = r.AsString(7),
        Operation = (ChangeOperation)r.GetInt64(8),
        ChangedBy = r.GetString(9),
        ChangedAtUtc = DateTimeOffset.Parse(r.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
    };

    private void EnsureTable() => _db.Locked(() => _db.Connection.Execute(
        $"""
        CREATE TABLE IF NOT EXISTS {Table} (
          client_seq    INTEGER PRIMARY KEY AUTOINCREMENT,
          change_id     TEXT NOT NULL,
          change_set_id TEXT NOT NULL,
          table_name    TEXT NOT NULL,
          row_id        TEXT NOT NULL,
          column_name   TEXT NOT NULL,
          old_value     TEXT,
          new_value     TEXT,
          operation     INTEGER NOT NULL,
          changed_by    TEXT NOT NULL,
          changed_at    TEXT NOT NULL
        )
        """));
}
