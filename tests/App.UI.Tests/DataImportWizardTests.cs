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
/// Integration coverage for the wired Data Import wizard: drive <see cref="DataImportState"/> through the
/// real pipeline — parse an uploaded .xlsx with the shared workbook reader, then load it through the
/// shared <see cref="ImportEngine"/> into the store — proving the wizard performs a real import (not the
/// previous mock).
/// </summary>
public sealed class DataImportWizardTests
{
    private static DataImportState NewState(out FakeLocalStore store)
    {
        FakeCatalog catalog = new(DefaultCatalog.Entries());
        store = new FakeLocalStore();
        FunctionLibrary functions = new();
        ImportEngine engine = new(catalog, store, new RuleExpressionBuilder(functions, new ExpressionClassifier(functions)));
        return new DataImportState(new LanguageState(), catalog, engine, new ClosedXmlWorkbookReader());
    }

    private static MemoryStream BuildWorkbook()
    {
        MemoryStream stream = new();
        using (XLWorkbook workbook = new())
        {
            IXLWorksheet apps = workbook.Worksheets.Add("Apps");
            apps.Cell(1, 1).Value = "AppCode";
            apps.Cell(1, 2).Value = "Nom GDM";
            apps.Cell(1, 3).Value = "Junk";
            apps.Cell(2, 1).Value = "GDM1"; apps.Cell(2, 2).Value = "Gold DM"; apps.Cell(2, 3).Value = "x";

            IXLWorksheet source = workbook.Worksheets.Add("Source");
            source.Cell(1, 1).Value = "Nom";
            source.Cell(1, 2).Value = "Code applicatif du système";
            source.Cell(2, 1).Value = "Orders"; source.Cell(2, 2).Value = "GDM1";

            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task Uploading_a_workbook_parses_worksheets_and_a_preview()
    {
        DataImportState state = NewState(out _);
        using MemoryStream workbook = BuildWorkbook();

        await state.LoadFileAsync("workbook.xlsx", workbook.Length, workbook);

        Assert.True(state.HasFile);
        Assert.Null(state.ParseError);
        Assert.Contains(state.Worksheets(), w => w.Name == "Apps" && w.Table == TableNames.Application && w.Rows == 1);
        Assert.Contains(state.Worksheets(), w => w.Name == "Source" && w.Table == TableNames.DataSource);

        // The "Junk" column is not in the mapping, so it's reported as ignored on the Apps sheet.
        WorksheetVm apps = state.Worksheets().First(w => w.Name == "Apps");
        Assert.Contains("Junk", apps.UnmappedHeaders);
    }

    [Fact]
    public async Task RunImport_writes_rows_to_the_store_and_resolves_references()
    {
        DataImportState state = NewState(out FakeLocalStore store);
        using MemoryStream workbook = BuildWorkbook();
        await state.LoadFileAsync("workbook.xlsx", workbook.Length, workbook);

        state.RunImport();

        Assert.NotNull(state.Report);
        Assert.True(state.Done);
        Assert.Equal(2, state.Report!.TotalCreated); // one app + one source

        Row app = store.GetAll(TableNames.Application).Single();
        Assert.Equal("GDM1", app["app_code"]);
        Assert.Equal("Gold DM", app["name_gdm"]);

        Row src = store.GetAll(TableNames.DataSource).Single();
        Assert.Equal("Orders", src["name"]);
        Assert.Equal(app.Id.ToString(), src["application_id"]); // FK resolved by natural key during import
    }

    [Fact]
    public async Task Validate_flags_rows_missing_a_required_value()
    {
        DataImportState state = NewState(out _);
        // Apps sheet where one row has no AppCode (required app_code) -> would be skipped.
        MemoryStream stream = new();
        using (XLWorkbook wb = new())
        {
            IXLWorksheet apps = wb.Worksheets.Add("Apps");
            apps.Cell(1, 1).Value = "AppCode"; apps.Cell(1, 2).Value = "Nom GDM";
            apps.Cell(2, 1).Value = "GDM1"; apps.Cell(2, 2).Value = "Has code";
            apps.Cell(3, 2).Value = "No code"; // missing AppCode
            wb.SaveAs(stream);
        }

        stream.Position = 0;
        await state.LoadFileAsync("workbook.xlsx", stream.Length, stream);

        ValidationVm appsCheck = state.Validate().First(v => v.Worksheet == "Apps");
        Assert.Equal(2, appsCheck.Rows);
        Assert.Equal(1, appsCheck.WouldImport);
        Assert.Equal(1, appsCheck.WouldSkip);
    }
}
