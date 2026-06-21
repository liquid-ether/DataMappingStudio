using System.Text;
using App.Application.Abstractions;
using App.Domain.Catalog;
using App.Domain.Data;

namespace App.Infrastructure.Remote.Formats;

/// <summary>
/// CSV format (RFC 4180-ish): zero runtime dependency, universal, append-friendly. Untyped on read —
/// columns come back as <see cref="CatalogValueType.Text"/>; callers apply catalog types (Architecture §6a).
/// </summary>
public sealed class CsvRemoteFormat : IRemoteFormat
{
    public string Name => "csv";

    public string Extension => ".csv";

    public void Write(string path, RemoteTable table)
    {
        StringBuilder sb = new();
        sb.AppendLine(string.Join(",", table.Columns.Select(c => Escape(c.Name))));
        foreach (IReadOnlyList<string?> row in table.Rows)
        {
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public RemoteTable Read(string path)
    {
        using StreamReader reader = new(path, Encoding.UTF8);
        List<List<string?>> records = ParseRecords(reader);
        if (records.Count == 0)
        {
            return RemoteTable.Empty([]);
        }

        List<RemoteColumn> columns = records[0].Select(name => new RemoteColumn(name ?? string.Empty, CatalogValueType.Text)).ToList();
        List<IReadOnlyList<string?>> rows = records.Skip(1).Cast<IReadOnlyList<string?>>().ToList();
        return new RemoteTable(columns, rows);
    }

    private static string Escape(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        bool needsQuote = value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r');
        string escaped = value.Replace("\"", "\"\"");
        return needsQuote ? $"\"{escaped}\"" : escaped;
    }

    private static List<List<string?>> ParseRecords(TextReader reader)
    {
        List<List<string?>> records = [];
        List<string?> current = [];
        StringBuilder field = new();
        bool inQuotes = false;
        bool fieldStarted = false;
        int c;

        void EndField()
        {
            current.Add(field.Length == 0 && !fieldStarted ? null : field.ToString());
            field.Clear();
            fieldStarted = false;
        }

        void EndRecord()
        {
            EndField();
            records.Add(current);
            current = [];
        }

        while ((c = reader.Read()) != -1)
        {
            char ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else { inQuotes = false; }
                }
                else { field.Append(ch); }
            }
            else
            {
                switch (ch)
                {
                    case '"': inQuotes = true; fieldStarted = true; break;
                    case ',': EndField(); break;
                    case '\r': break;
                    case '\n': EndRecord(); break;
                    default: field.Append(ch); fieldStarted = true; break;
                }
            }
        }

        if (field.Length > 0 || current.Count > 0 || fieldStarted)
        {
            EndRecord();
        }

        return records;
    }
}
