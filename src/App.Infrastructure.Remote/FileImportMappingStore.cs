using System.Text.Json;
using System.Text.RegularExpressions;
using App.Application.Importing;

namespace App.Infrastructure.Remote;

/// <summary>
/// Saved import mappings as one JSON document per name under
/// <c>&lt;remote&gt;/_meta/import-mappings/</c>. Atomic writes + the usual sync-client retry; names are
/// restricted to filename-safe characters so a mapping name can never escape the folder.
/// </summary>
public sealed partial class FileImportMappingStore : IImportMappingStore
{
    private sealed record Document(string Name, string SavedBy, DateTimeOffset SavedAtUtc, ImportMapping Mapping);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly string _dir;

    public FileImportMappingStore(string rootFolder)
    {
        _dir = Path.Combine(rootFolder, "_meta", "import-mappings");
        Directory.CreateDirectory(_dir);
    }

    public IReadOnlyList<SavedMappingInfo> List() => RemoteIo.Retry(() =>
        Directory.EnumerateFiles(_dir, "*.json")
            .Select(ReadDocument)
            .OfType<Document>()
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new SavedMappingInfo(d.Name, d.SavedBy, d.SavedAtUtc))
            .ToList());

    public ImportMapping? Get(string name)
    {
        string path = PathFor(name);
        return File.Exists(path) ? RemoteIo.Retry(() => ReadDocument(path))?.Mapping : null;
    }

    public void Save(string name, ImportMapping mapping, string savedBy)
    {
        string path = PathFor(name); // validates the name
        string json = JsonSerializer.Serialize(new Document(name.Trim(), savedBy, DateTimeOffset.UtcNow, mapping), Json);
        RemoteIo.Retry(() => AtomicWrite.Write(path, temp => File.WriteAllText(temp, json)));
    }

    public bool Delete(string name)
    {
        string path = PathFor(name); // validates the name
        if (!File.Exists(path))
        {
            return false;
        }

        RemoteIo.Retry(() => File.Delete(path));
        return true;
    }

    private static Document? ReadDocument(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return null; // a malformed/mid-sync file never breaks the listing
        }
    }

    private string PathFor(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length is 0 or > 64 || !SafeName().IsMatch(trimmed))
        {
            throw new ArgumentException(
                $"Invalid mapping name '{name}'. Use letters, digits, spaces, dashes or underscores (max 64 characters).");
        }

        return Path.Combine(_dir, trimmed + ".json");
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 _-]*$")]
    private static partial Regex SafeName();
}
