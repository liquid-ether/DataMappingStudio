using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using App.Application.Abstractions;
using App.Domain.Catalog;

namespace App.Infrastructure.Remote;

/// <summary>
/// Per-writer meta-model change logs on the synced folder: <c>_meta/catalog/&lt;writer-id&gt;.json</c>.
/// Same invariant as the data logs (Architecture §6a): only the owning writer ever writes their file, so
/// appends never collide. JSON (not the tabular <c>IRemoteFormat</c>) because entries are hierarchical,
/// tiny, and benefit from being human-diffable in the shared folder.
/// </summary>
public sealed class FileCatalogRemote : ICatalogRemote
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _dir;

    public FileCatalogRemote(string rootFolder)
    {
        _dir = Path.Combine(rootFolder, "_meta", "catalog");
        Directory.CreateDirectory(_dir);
    }

    public string CatalogVersion() => RemoteIo.Retry(() =>
    {
        if (!Directory.Exists(_dir))
        {
            return "0";
        }

        StringBuilder sb = new();
        foreach (string file in Directory.EnumerateFiles(_dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            FileInfo info = new(file);
            sb.Append(info.Name).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append('|');
        }

        return sb.ToString();
    });

    public IReadOnlyList<CatalogChangeEntry> ReadAll() => RemoteIo.Retry(() =>
        Directory.EnumerateFiles(_dir, "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(ReadFile)
            .ToList());

    public IReadOnlyList<CatalogChangeEntry> ReadWriter(string writerId)
    {
        string path = LogPath(writerId);
        return File.Exists(path) ? RemoteIo.Retry(() => ReadFile(path)) : [];
    }

    public void Append(string writerId, IReadOnlyList<CatalogChangeEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        List<CatalogChangeEntry> all = [.. ReadWriter(writerId), .. entries];
        string json = JsonSerializer.Serialize(all, Json);
        RemoteIo.Retry(() => AtomicWrite.Write(LogPath(writerId), temp => File.WriteAllText(temp, json)));
    }

    private static IReadOnlyList<CatalogChangeEntry> ReadFile(string path)
        => JsonSerializer.Deserialize<List<CatalogChangeEntry>>(File.ReadAllText(path), Json) ?? [];

    private string LogPath(string writerId) => Path.Combine(_dir, writerId + ".json");
}
