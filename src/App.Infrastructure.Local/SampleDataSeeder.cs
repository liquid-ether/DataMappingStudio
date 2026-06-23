using App.Application.Provisioning;
using App.Domain.Data;
using App.Domain.Entities;
using App.Infrastructure.Local.Sqlite;

namespace App.Infrastructure.Local;

/// <summary>
/// Writes a generated <see cref="SampleDataSet"/> into the local working copy as a canonical baseline.
/// Rows are inserted directly (not via the change log) in a single transaction, so seeding is fast and
/// does not surface thousands of "pending" edits in the publish flow — the sample data is treated as
/// already-published state. Run after <see cref="ILocalStore.EnsureSchema"/>.
/// </summary>
public sealed class SampleDataSeeder(LocalDatabase db)
{
    /// <summary>Seeds the demo dataset only when the store is empty; returns the number of rows written (0 if skipped).</summary>
    public int SeedIfEmpty(int seed = 20260623)
    {
        bool hasData = db.Connection.Scalar($"SELECT 1 FROM {Q(TableNames.Application)} LIMIT 1") is not null;
        return hasData ? 0 : Seed(SampleData.Build(seed));
    }

    public int Seed(SampleDataSet data)
    {
        int written = 0;
        // BEGIN/COMMIT via raw SQL (not a SqliteTransaction object) so the helper commands need no
        // explicit Transaction assignment while still committing once — fast bulk insert.
        db.Connection.Execute("BEGIN");
        try
        {
            foreach ((string table, IReadOnlyList<Row> rows) in data.AllTables())
            {
                foreach (Row row in rows)
                {
                    Insert(table, row);
                    written++;
                }
            }

            db.Connection.Execute("COMMIT");
        }
        catch
        {
            db.Connection.Execute("ROLLBACK");
            throw;
        }

        return written;
    }

    private void Insert(string table, Row row)
    {
        List<string> columns = ["id"];
        List<string> placeholders = ["$id"];
        List<(string, object?)> parameters = [("$id", row.Id.ToString())];

        int i = 0;
        foreach ((string column, string? value) in row.Values)
        {
            columns.Add(column);
            placeholders.Add($"$v{i}");
            parameters.Add(($"$v{i}", value));
            i++;
        }

        db.Connection.Execute(
            $"INSERT INTO {Q(table)} ({string.Join(", ", columns.Select(Q))}) VALUES ({string.Join(", ", placeholders)})",
            [.. parameters]);
    }

    private static string Q(string identifier) => SqlIdentifier.Quote(identifier);
}
