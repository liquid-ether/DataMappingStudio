using System.Text.Json;
using App.Application.Abstractions;

namespace App.Infrastructure.Remote;

/// <summary>
/// Shared runtime settings as one JSON document at <c>&lt;remote&gt;/_meta/settings.json</c>. Whole-file
/// last-writer-wins is deliberate: settings writes are rare, admin-only operations, and losing one
/// concurrent save is harmless (documented on the Settings page) — per-writer folding would be overkill.
/// Writes are atomic (temp + rename) with the usual sync-client retry.
/// </summary>
public sealed class FileSettingsStore : ISharedSettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;

    public FileSettingsStore(string rootFolder)
    {
        string dir = Path.Combine(rootFolder, "_meta");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public string Version() => RemoteIo.Retry(() =>
    {
        FileInfo info = new(_path);
        return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : "0";
    });

    public SettingsDocument? Read() => RemoteIo.Retry(() =>
        File.Exists(_path)
            ? JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(_path), Json)
            : null);

    public void Write(SettingsDocument document)
    {
        string json = JsonSerializer.Serialize(document, Json);
        RemoteIo.Retry(() => AtomicWrite.Write(_path, temp => File.WriteAllText(temp, json)));
    }
}
