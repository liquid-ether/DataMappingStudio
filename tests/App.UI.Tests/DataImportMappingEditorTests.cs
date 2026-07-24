using App.Application.Expressions;
using App.Application.Importing;
using App.Application.Provisioning;
using App.Application.Sync;
using App.Domain.Catalog;
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

        public bool Delete(string name) => _saved.Remove(name);
    }

    private static DataImportState NewState(out FakeCatalog catalog, IImportMappingStore? mappings = null, CatalogSyncService? catalogSync = null, LanguageState? lang = null)
    {
        catalog = new FakeCatalog(DefaultCatalog.Entries());
        FunctionLibrary functions = new();
        FakeLocalStore store = new();
        ImportEngine engine = new(catalog, store, new RuleExpressionBuilder(functions, new ExpressionClassifier(functions)));
        return new DataImportState(lang ?? new LanguageState(), catalog, engine, new ClosedXmlWorkbookReader(),
            new App.Application.Abstractions.EnvironmentCurrentUser(), mappings: mappings,
            tables: catalogSync is null ? null : new FakeTableCatalog(), catalogSync: catalogSync,
            store: catalogSync is null ? null : store);
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
        (DataImportState state, _) = await LoadedState(mappings, catalogSync: null);
        return state;
    }

    private static async Task<(DataImportState State, FakeCatalog Catalog)> LoadedState(IImportMappingStore? mappings, CatalogSyncService? catalogSync, LanguageState? lang = null)
    {
        DataImportState state = NewState(out FakeCatalog catalog, mappings, catalogSync, lang);
        using MemoryStream workbook = BuildWorkbook();
        await state.LoadFileAsync("workbook.xlsx", workbook.Length, workbook);
        Assert.Null(state.ParseError);
        return (state, catalog);
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
    public async Task A_column_added_after_upload_is_offered_and_auto_mappable()
    {
        (DataImportState state, FakeCatalog catalog) = await LoadedState(null, catalogSync: null);
        SheetMappingDraft extra = state.Drafts.Single(d => d.Sheet == "Extra");
        state.SetDraftTable(extra, TableNames.Application);
        Dictionary<string, string> manualPicks = new(extra.HeaderToColumn);

        // The model grows AFTER the file was uploaded and the sheet mapped (the reported repro).
        catalog.AddColumn(new ColumnCatalogEntry
        {
            TableName = TableNames.Application,
            ColumnName = "mystery_col",
            ValueType = CatalogValueType.Text,
            LabelEn = "Mystery",
            LabelFr = "Mystère",
            IsUserAdded = true,
        });

        // The dropdown reads the live catalog, so the new column is immediately offered…
        Assert.Contains(state.MappableColumns(TableNames.Application), c => c.ColumnName == "mystery_col");

        // …and the explicit auto-map matches the header by label without touching earlier picks.
        state.AutoMapNewColumns(extra);
        Assert.Equal("mystery_col", extra.HeaderToColumn["Mystery"]);
        foreach ((string header, string column) in manualPicks)
        {
            Assert.Equal(column, extra.HeaderToColumn[header]);
        }
    }

    [Fact]
    public async Task A_catalog_publish_notifies_the_wizard_and_refresh_re_automaps()
    {
        CatalogSyncService catalogSync = new(new FakeCatalogRemote());
        (DataImportState state, FakeCatalog catalog) = await LoadedState(null, catalogSync);
        SheetMappingDraft extra = state.Drafts.Single(d => d.Sheet == "Extra");
        state.SetDraftTable(extra, TableNames.Application);

        bool notified = false;
        state.CatalogModelChanged += () => notified = true;

        // Another circuit publishes a new column into the shared catalog log.
        catalogSync.Publish("admin",
        [
            new CatalogChangeEntry
            {
                ClientSeq = 0,
                ChangedBy = "admin",
                ChangedAtUtc = DateTimeOffset.UtcNow,
                Kind = CatalogChangeKind.ColumnAdded,
                Column = new ColumnCatalogEntry
                {
                    TableName = TableNames.Application,
                    ColumnName = "mystery_col",
                    ValueType = CatalogValueType.Text,
                    LabelEn = "Mystery",
                    LabelFr = "Mystère",
                    IsUserAdded = true,
                },
            },
        ]);

        Assert.True(notified); // the wizard would now marshal RefreshFromCatalog onto its dispatcher

        state.RefreshFromCatalog();
        Assert.Contains(catalog.GetForTable(TableNames.Application), c => c.ColumnName == "mystery_col"); // fold applied locally
        Assert.Equal("mystery_col", extra.HeaderToColumn["Mystery"]); // and the header auto-mapped

        // Dispose unsubscribes from the host-wide event (no leak across circuits).
        state.Dispose();
        notified = false;
        catalogSync.Publish("admin", [new CatalogChangeEntry { ClientSeq = 0, ChangedBy = "admin", ChangedAtUtc = DateTimeOffset.UtcNow, Kind = CatalogChangeKind.TableMetaUpdated, Table = new TableCatalogEntry { TableName = TableNames.Application } }]);
        Assert.False(notified);
    }

    [Fact]
    public async Task Column_options_show_bilingual_labels_with_the_column_name()
    {
        LanguageState lang = new();
        (DataImportState state, FakeCatalog catalog) = await LoadedState(null, catalogSync: null, lang);
        catalog.AddColumn(new ColumnCatalogEntry
        {
            TableName = TableNames.Application,
            ColumnName = "tier",
            ValueType = CatalogValueType.Text,
            LabelEn = "Tier",
            LabelFr = "Niveau",
            IsRequired = true,
            IsUserAdded = true,
        });
        ColumnCatalogEntry entry = state.MappableColumns(TableNames.Application).Single(c => c.ColumnName == "tier");

        Assert.Equal("Tier (tier) *", state.ColumnOptionLabel(entry));
        lang.Set("fr");
        Assert.Equal("Niveau (tier) *", state.ColumnOptionLabel(entry));

        // Label == name (the default when none was entered): no redundant parenthesis.
        ColumnCatalogEntry plain = entry with { LabelEn = "tier", LabelFr = "tier", IsRequired = false };
        Assert.Equal("tier", state.ColumnOptionLabel(plain));
    }

    [Fact]
    public async Task A_saved_mapping_that_no_longer_matches_the_model_is_flagged()
    {
        InMemoryMappingStore store = new();
        store.Save("Stale", new ImportMapping
        {
            Worksheets =
            [
                new WorksheetMapping
                {
                    Worksheet = "Apps",
                    Table = "ghost_table", // vanished (or never synced here)
                    NaturalKey = ["app_code"],
                    Columns = new Dictionary<string, string> { ["AppCode"] = "app_code" },
                },
                new WorksheetMapping
                {
                    Worksheet = "Extra",
                    Table = TableNames.Application,
                    NaturalKey = ["app_code"],
                    Columns = new Dictionary<string, string> { ["app_code"] = "app_code", ["Name GDM"] = "no_such_col" },
                },
            ],
        }, "x");
        DataImportState state = await LoadedState(store);

        state.LoadSavedMapping("Stale");

        Assert.False(state.MappingReady); // gated instead of failing at import time
        IReadOnlyList<string> errors = state.MappingErrors();
        Assert.Contains(errors, e => e.Contains("ghost_table"));
        Assert.Contains(errors, e => e.Contains("no_such_col"));
    }

    [Fact]
    public async Task Deleting_a_saved_mapping_updates_the_cached_list()
    {
        InMemoryMappingStore store = new();
        DataImportState state = await LoadedState(store);
        state.SaveMapping("Doomed");
        Assert.Single(state.SavedMappings());

        state.DeleteMapping("Doomed");

        Assert.Empty(state.SavedMappings());
        Assert.Contains("Doomed", state.MappingMessage);

        state.DeleteMapping("Never existed");
        Assert.Contains("Never existed", state.MappingMessage);
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
