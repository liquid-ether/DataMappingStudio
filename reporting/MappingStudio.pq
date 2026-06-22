// Power Query (M) template for Mapping Studio reporting (Architecture §11).
// Enumerates the synced canonical folder and loads every materialized table snapshot — and every
// opt-in denormalized "<table>_report" view if present — so new tables/columns flow through
// automatically. Parameterized by the format; swap LoadParquet for the CSV/Excel loader if needed.
//
// Usage in Power BI / Excel: set RemoteFolder, then reference this query. Each file becomes a table.
let
    RemoteFolder = "C:\OneDrive\MappingStudio",   // <-- point at the synced folder
    Extension = ".parquet",                        // ".parquet" | ".csv" | ".xlsx"

    Files = Folder.Files(RemoteFolder),
    Snapshots = Table.SelectRows(
        Files,
        each Text.EndsWith(Text.Lower([Name]), Extension)
            and not Text.StartsWith([Name], "_")    // skip _changes / _meta side files
    ),
    Loaded = Table.AddColumn(Snapshots, "Data", each
        if Extension = ".parquet" then Parquet.Document([Content])
        else if Extension = ".csv" then Csv.Document([Content], [Delimiter = ",", Encoding = 65001, QuoteStyle = QuoteStyle.Csv])
        else Excel.Workbook([Content]){0}[Data]
    ),
    Tables = Table.SelectColumns(
        Table.AddColumn(Loaded, "Table", each Text.BeforeDelimiter([Name], ".")),
        {"Table", "Data"}
    )
in
    Tables
