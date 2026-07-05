namespace App.Application.Importing;

/// <summary>A saved mapping's card in pickers/listings.</summary>
public sealed record SavedMappingInfo(string Name, string SavedBy, DateTimeOffset SavedAtUtc);

/// <summary>
/// Named import mappings, authored in the wizard's Mapping step and shared through the synced folder
/// (<c>_meta/import-mappings/&lt;name&gt;.json</c>) — the single source both the wizard and the CLI
/// importer consume, so a mapping saved once drives every import path. Saves are whole-file per name,
/// last-writer-wins (mapping edits are rare, deliberate operations).
/// </summary>
public interface IImportMappingStore
{
    IReadOnlyList<SavedMappingInfo> List();

    ImportMapping? Get(string name);

    void Save(string name, ImportMapping mapping, string savedBy);
}
