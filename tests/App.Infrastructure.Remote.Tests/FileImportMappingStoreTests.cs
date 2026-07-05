using App.Application.Importing;

namespace App.Infrastructure.Remote.Tests;

/// <summary>
/// The shared named-mapping store (<c>_meta/import-mappings</c>): mappings saved from the wizard round-trip
/// with full fidelity (columns, natural key, references, expression column) and are the only thing the CLI
/// importer can run — so the contract here is the wizard→CLI hand-off.
/// </summary>
public sealed class FileImportMappingStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dms-mappings-" + Guid.NewGuid().ToString("N"));

    public FileImportMappingStoreTests() => Directory.CreateDirectory(_dir);

    private static ImportMapping SampleMapping() => new()
    {
        Worksheets =
        [
            new WorksheetMapping
            {
                Worksheet = "Apps",
                Table = "application",
                NaturalKey = ["app_code"],
                Columns = new Dictionary<string, string> { ["AppCode"] = "app_code", ["Nom GDM"] = "name_gdm" },
                References = new Dictionary<string, ColumnReference>(),
            },
            new WorksheetMapping
            {
                Worksheet = "Source",
                Table = "data_source",
                NaturalKey = ["name"],
                Columns = new Dictionary<string, string> { ["Nom"] = "name", ["Code applicatif du système"] = "application_id" },
                References = new Dictionary<string, ColumnReference> { ["application_id"] = new("application", "app_code") },
                ExpressionColumn = "expression",
            },
        ],
    };

    [Fact]
    public void Save_get_list_round_trip_preserves_the_full_mapping()
    {
        FileImportMappingStore store = new(_dir);

        store.Save("Team workbook", SampleMapping(), savedBy: "mathieu");

        SavedMappingInfo info = Assert.Single(store.List());
        Assert.Equal("Team workbook", info.Name);
        Assert.Equal("mathieu", info.SavedBy);

        // A second store over the same folder (another machine via the synced folder) sees the mapping.
        ImportMapping? loaded = new FileImportMappingStore(_dir).Get("Team workbook");
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Worksheets.Count);
        WorksheetMapping source = loaded.Worksheets.Single(w => w.Worksheet == "Source");
        Assert.Equal("data_source", source.Table);
        Assert.Equal(new[] { "name" }, source.NaturalKey);
        Assert.Equal("application_id", source.Columns["Code applicatif du système"]);
        Assert.Equal(new ColumnReference("application", "app_code"), source.References["application_id"]);
        Assert.Equal("expression", source.ExpressionColumn);
    }

    [Fact]
    public void Saving_the_same_name_overwrites_and_unknown_names_return_null()
    {
        FileImportMappingStore store = new(_dir);
        store.Save("Nightly", SampleMapping(), "a");
        store.Save("Nightly", new ImportMapping { Worksheets = [] }, "b");

        Assert.Empty(store.Get("Nightly")!.Worksheets);
        Assert.Equal("b", Assert.Single(store.List()).SavedBy);
        Assert.Null(store.Get("Never saved"));
    }

    [Fact]
    public void Unsafe_names_are_rejected_and_malformed_files_never_break_the_listing()
    {
        FileImportMappingStore store = new(_dir);
        Assert.Throws<ArgumentException>(() => store.Save("../escape", SampleMapping(), "x"));
        Assert.Throws<ArgumentException>(() => store.Save("", SampleMapping(), "x"));
        Assert.Throws<ArgumentException>(() => store.Save(new string('a', 65), SampleMapping(), "x"));

        store.Save("Good", SampleMapping(), "x");
        File.WriteAllText(Path.Combine(_dir, "_meta", "import-mappings", "broken.json"), "{ not json");

        Assert.Equal("Good", Assert.Single(store.List()).Name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
