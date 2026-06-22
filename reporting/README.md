# Reporting (read-only)

Reporting consumes the **materialized remote outputs only**, read-only — the app (via its publish/fold
logic) is the sole writer, so reporting can never corrupt canonical data (Architecture §11).

Two kinds of output live in the synced canonical folder:

- **Normalized table snapshots** — `<table>.<ext>`, one per table, FKs intact. Rebuilt as part of every
  publish/fold, or on demand with `build/rebuild-snapshots.ps1`.
- **Denormalized reporting views (opt-in)** — `<table>_report.<ext>`, with every reference column
  resolved to its display value and every computed column evaluated, flattened so no joins are needed.
  Build them with `build/rebuild-snapshots.ps1` then the `report` verb, or directly:

  ```powershell
  dotnet run --project src/App.Importer -- report <dbPath> <remoteFolder> [parquet|csv|excel]
  ```

## Connecting Power BI / Excel

1. Open **Power BI Desktop** (or Excel → Data → Get Data → Blank Query → Advanced Editor).
2. Paste [`MappingStudio.pq`](MappingStudio.pq), set `RemoteFolder` to your synced folder and `Extension`
   to the configured `RemoteFormat`.
3. The query returns one row per snapshot/`*_report` file with an expandable `Data` table — load the
   tables you need. New tables and columns appear automatically on refresh.

Parquet carries types natively; for CSV the catalog types are applied by Power Query; Excel snapshots
are read directly. A `.pbit` template can wrap this query for one-click distribution.
