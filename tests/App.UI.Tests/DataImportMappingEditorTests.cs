using App.Application.Expressions;
using App.Application.Importing;
using App.Application.Provisioning;
using App.Domain.Data;
using App.Domain.Entities;
using App.Infrastructure.Local;
using App.UI.DataImport;
using App.UI.Localization;
using ClosedXML.Excel;

namespace App.UI.Tests;

/// <summary>
/// The wizard's data-driven Mapping step: worksheets become editable drafts (prefilled from the built-in
/// template), any sheet can be pointed at any catalog table with header auto-mapping, and the result can
/// be saved to / loaded from the shared mapping store — the same store the CLI importer runs from.
/// </summary>
public sealed class DataImportMappingEditorTests
{
    private sealed class InMemoryMappingStore : IImportMappingStore
    {
        private readonly Dictionary<string, (ImportMapping Mapping, string SavedBy)> _saved = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<SavedMappingInfo> List()
            => _saved.Select(kv => new SavedMappingInfo(kv.Key, kv.Value.SavedBy, DateTimeOffset.UtcNow)).ToList();

        public ImportMapping? Get(string name) => _saved.TryGetValue(name, out (ImportMapping Mapping, string SavedBy) doc) ? doc.Mapping : null;

        public void Save(string name, ImportMapping mapping, string savedBy) => _saved[name] = (mapping, savedBy);
    }

    private static DataImportState NewState(IImportMappingStore? mappings = null)
    {
        FakeCatalog catalog = new(DefaultCatalog.Entries());
        FunctionLibrary functions = new();
        ImportEngine engine = new(catalog, new FakeLocalStore(), new RuleExpressionBuilder(functions, new ExpressionClassifier(functions)));
        return new DataImportState(new LanguageState(), catalog, engine, new ClosedXmlWorkbookReader(), new App.Application.Abstractions.EnvironmentCurrentUser(), mappings: mappings);
    }

    /// <summary>Apps (known to the built-in template) + Extra (unknown, with headers matching application columns).</summary>
    private static MemoryStream BuildWorkbook()
    {
        MemoryStream stream = new();
        using (XLWorkbook workbook = new())
        {
            IXLWorksheet apps = workbook.Worksheets.Add("Apps");
            apps.Cell(1, 1).Value = "AppCode"; apps.Cell(1, 2).Value = "Nom GDM";
            apps.Cell(2, 1).Value = "GDM1"; apps.Cell(2, 2).Value = "Gold DM";

            IXLWorksheet extra = workbook.Worksheets.Add("Extra");
            extra.Cell(1, 1).Value = "app_code"; extra.Cell(1, 2).Value = "Name GDM"; extra.Cell(1, 3).Value = "Mystery";
            extra.Cell(2, 1).Value = "X1"; extra.Cell(2, 2).Value = "Extra app"; extra.Cell(2, 3).Value = "?";
            extra.Cell(3, 1).Value = "X2"; extra.Cell(3, 2).Value = "Other app"; extra.Cell(3, 3).Value = "?";

            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    private static async Task<DataImportState> LoadedState(IImportMappingStore? mappings = null)
    {
        DataImportState state = NewState(mappings);
        using MemoryStream workbook = BuildWorkbook();
        await state.LoadFileAsync("workbook.xlsx", workbook.Length, workbook);
        Assert.Null(state.ParseError);
        return state;
    }

    [Fact]
    public async Task Every_sheet_gets_a_draft_prefilled_from_the_builtin_template()
    {
        DataImportState state = await LoadedState();

        Assert.Equal(2, state.Drafts.Count);
        SheetMappingDraft apps = state.Drafts.Single(d => d.Sheet == "Apps");
        Assert.Equal(TableNames.Application, apps.Table); // recognized by the built-in template
        Assert.Equal("app_code", apps.HeaderToColumn["AppCode"]);
        Assert.Contains("app_code", apps.NaturalKey);

        SheetMappingDraft extra = state.Drafts.Single(d => d.Sheet == "Extra");
        Assert.False(extra.IsMapped); // unknown sheet: present, editable, not imported by default
        Assert.Single(state.Mapping.Worksheets);
        Assert.Equal(1, state.TotalRows); // only mapped sheets count toward the import
    }

    [Fact]
    public async Task Choosing_a_table_auto_maps_headers_and_defaults_the_natural_key()
    {
        DataImportState state = await LoadedState();
        SheetMappingDraft extra = state.Drafts.Single(d => d.Sheet == "Extra");

        state.SetDraftTable(extra, TableNames.Application);

        Assert.Equal("app_code", extra.HeaderToColumn["app_code"]);   // exact column-name match
        Assert.Equal("name_gdm", extra.HeaderToColumn["Name GDM"]);   // label match, case/space-insensitive
        Assert.False(extra.HeaderToColumn.ContainsKey("Mystery"));    // no match -> ignored
        Assert.Contains("app_code", extra.NaturalKey);                // required mapped column
        Assert.True(state.MappingReady);
        Assert.Equal(3, state.TotalRows); // both sheets now import

        // Clearing the table takes the sheet back out of the import.
        state.SetDraftTable(extra, "");
        Assert.False(extra.IsMapped);
        Assert.Equal(1, state.TotalRows);
    }

    [Fact]
    public async Task A_mapped_sheet_without_a_key_blocks_the_next_step()
    {
        DataImportState state = await LoadedState();
        SheetMappingDraft apps = state.Drafts.Single(d => d.Sheet == "Apps");
        state.GoNext(); // Source -> Mapping
        Assert.Equal(1, state.Step);

        foreach (string key in apps.NaturalKey.ToList())
        {
            state.ToggleNaturalKey(apps, key);
        }

        Assert.False(state.MappingReady);
        Assert.NotEmpty(state.MappingErrors());
        state.GoNext();
        Assert.Equal(1, state.Step); // still on Mapping

        state.ToggleNaturalKey(apps, "app_code");
        Assert.True(state.MappingReady);
        state.GoNext();
        Assert.Equal(2, state.Step);
    }

    [Fact]
    public async Task Remapping_a_header_keeps_one_feeder_per_column_and_prunes_the_key()
    {
        DataImportState state = await LoadedState();
        SheetMappingDraft apps = state.Drafts.Single(d => d.Sheet == "Apps");

        // Point the second header at the column the first one feeds: the first loses it.
        state.SetHeaderColumn(apps, "Nom GDM", "app_code");
        Assert.False(apps.HeaderToColumn.ContainsKey("AppCode"));
        Assert.Equal("app_code", apps.HeaderToColumn["Nom GDM"]);
        Assert.Contains("app_code", apps.NaturalKey); // still mapped, key survives

        // Unmapping the feeder drops the column from the natural key too.
        state.SetHeaderColumn(apps, "Nom GDM", "");
        Assert.DoesNotContain("app_code", apps.NaturalKey);
    }

    [Fact]
    public async Task Save_and_load_round_trip_through_the_shared_store()
    {
        InMemoryMappingStore store = new();
        DataImportState author = await LoadedState(store);
        SheetMappingDraft extra = author.Drafts.Single(d => d.Sheet == "Extra");
        author.SetDraftTable(extra, TableNames.Application);

        author.SaveMapping("My workbook");
        Assert.Contains("My workbook", author.MappingMessage);
        Assert.Equal("My workbook", Assert.Single(author.SavedMappings()).Name);

        // A different session (CLI or another user) applies the saved mapping to the same workbook.
        DataImportState consumer = await LoadedState(store);
        Assert.False(consumer.Drafts.Single(d => d.Sheet == "Extra").IsMapped);
        consumer.LoadSavedMapping("My workbook");
        SheetMappingDraft applied = consumer.Drafts.Single(d => d.Sheet == "Extra");
        Assert.Equal(TableNames.Application, applied.Table);
        Assert.Equal("app_code", applied.HeaderToColumn["app_code"]);
        Assert.Contains("app_code", applied.NaturalKey);

        consumer.LoadSavedMapping("Nope");
        Assert.Contains("Nope", consumer.MappingMessage);
    }

    [Fact]
    public async Task Saving_without_a_store_or_with_errors_reports_instead_of_throwing()
    {
        DataImportState stateless = await LoadedState(); // no store registered
        stateless.SaveMapping("Anything");
        Assert.NotNull(stateless.MappingMessage);
        Assert.Empty(stateless.SavedMappings());

        InMemoryMappingStore store = new();
        DataImportState broken = await LoadedState(store);
        SheetMappingDraft apps = broken.Drafts.Single(d => d.Sheet == "Apps");
        foreach (string key in apps.NaturalKey.ToList())
        {
            broken.ToggleNaturalKey(apps, key);
        }

        broken.SaveMapping("Broken");
        Assert.Empty(broken.SavedMappings()); // invalid mapping never reaches the shared folder
    }
}
