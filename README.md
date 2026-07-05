# Mapping Studio — Data Mapping & Lineage App

A **.NET 10 Blazor Hybrid** desktop application for **data mapping and lineage**, replacing the
team's shared Excel workbook. It runs entirely in the Windows user context (no admin), captures
mapping **metadata** (never the underlying data), and shares it between ~10 analysts through a
**git-like, field-level sync** over a OneDrive-synced SharePoint folder (per-writer append-only
change logs + deterministic fold). The published mapping + rule catalog is consumed downstream by
an ETL engine.

> Design is captured in [DataMappingLineageApp-Architecture.md](DataMappingLineageApp-Architecture.md)
> and [DataMappingLineageApp-Diagrams.md](DataMappingLineageApp-Diagrams.md). The UI direction is
> the [Mapping Studio mockup](Mapping%20Studio%20%E2%80%94%20mockup.html). The full build plan lives
> outside the repo in the approved plan file.

## Status

| | |
|---|---|
| Current version | **v1.10.0** — supply-chain gate, /health, a11y, deeper import validation |
| Build | `dotnet build DataMappingStudio.slnx` — clean |
| Tests | `dotnet test DataMappingStudio.slnx` — 212 passing (+5 E2E skipped unless `DMS_E2E=1`) |
| Install | see **[INSTALL.md](INSTALL.md)** — desktop `.exe` + host page, web host, shared-folder setup |

The desktop has a **working top-bar menu** that switches between all editors (Applications, Sources,
Dictionary, Config, Classification, Rules), the Mapping Studio, Lineage, the **Data Import** wizard,
History and Publish — the Blazor Hybrid shell is a non-routed view switcher mirroring the web host's
navigation. A **light/dark theme toggle** in the top bar flips an app-wide theme (`ThemeState`,
persisted in the browser) that every screen honours.

The **Data Import** tool (`/import`, App.UI.DataImport) is a five-step wizard — Source → Mapping →
Validate → Load → Recap — that performs a **real import into the local database**: upload the team's
`.xlsx`, it is parsed by the shared workbook reader using the built-in mapping, previewed and dry-run
validated against the column catalog, then loaded through the **same `ImportEngine` the CLI uses** into
the local SQLite working copy as one reviewable Import change set (re-running upserts by natural key,
never duplicating). The recap is the real `ImportReport` (created/updated/skipped + resolved references
+ ignored columns per worksheet). Its visual language (Inter + JetBrains Mono, teal accent, light/dark
surfaces) — ported from the approved "Data Import Tool" UI design — is the basis for the **global design
system** in `_content/App.UI/css/app.css`.

A **sample dataset** seeds automatically into a fresh desktop store: 10 applications, 100 data sources,
5000 dictionary entries (5–150 fields/source), classifications, lookups, rules, and a 30-target Mapping
Studio model whose lineage chains are up to 5 levels deep. To (re)load it into an existing database run
`build/seed-sample-data.ps1` (resets + reseeds), or `App.Importer seed-sample <local.db>`.

The **Mapping Studio and Lineage are driven by the real data model**: a target's sources are real
`data_source` rows and their fields come from the `dictionary_entry` columns (`CatalogQuery`). The
**Lineage** view picks a source from an autocomplete and draws only that source's scoped graph (the
targets it feeds, downstream, plus each target's other sources), so it stays lean with hundreds of
sources. The **Mapping** and entity grids use a **virtualized QuickGrid**, so thousands of mapping and
dictionary rows render without lag. Reference-picker options and computed-column values are derived
**once per load** (one read of each referenced/child table via the bulk `EvaluateColumn`), not per
cell, so scrolling the large Sources/Dictionary tables stays smooth.

The shell is a **full-height flex layout**, so each table auto-sizes to take most of the window and
re-flows on resize. The breathing room around the content is configurable via CSS variables in
`src/App.UI/wwwroot/css/app.css`: `--buffer-x` (sides), `--buffer-top`, `--buffer-bottom`, and
`--content-max` (optional width cap; `none` = fill).

The desktop distributable is `MappingStudio.exe` + a tiny `wwwroot\index.html` host page, in two
flavours: **self-contained** (~70 MB, no install) and **compact** (~41 MB, needs the .NET 10 Desktop
Runtime) — `build/publish-desktop.ps1` [`-FrameworkDependent`]. Validate a build with
`build/smoke-desktop.ps1`; the desktop writes a detailed log per launch to
`%LOCALAPPDATA%\MappingStudio\logs`.

The **Mapping Studio is fully persisted** now — both the mapping rows and the target/source (alias)
structure live in the local store and sync. The desktop app packages via `build/publish-desktop.ps1`;
the shared (synced) folder is configurable via the `MAPPINGSTUDIO_REMOTE` env var (desktop) /
`RemoteFolder` config (web).

**Mapping Studio persistence (`App.Application.Mappings`)** — the studio is now backed by the local
store: `MappingRepository` reads/writes rows to the `mapping` table via `ILocalStore`, so every
add/edit/delete becomes a change-log entry that the publish/sync flow pushes (verified: editing
mappings shows up as pending on `/publish` and in `/history`). The demo is seeded into the store on
first run; the target/source alias structure used for lineage context remains seeded configuration.

**References, computed columns & reporting (`App.Application.References` + `App.Infrastructure.Remote`)**
— reference-typed columns render as pickers and resolve to live display values (`ReferenceService`);
computed columns evaluate `count(...)` and `lookup(...)` autofills read-only; opt-in denormalized
**reporting views** (`ReportingViewBuilder` → `<table>_report.<ext>`) flatten references + computed for
Power BI (see [reporting/](reporting/)) — resolved in bulk, reading each referenced/child table once; and `LogCompactor` archives old change-log entries. The
importer CLI gained `report` and `compact` verbs.

**Importer & tooling (`App.Importer` + `build/`)** — the Excel→DB pipeline: the importer reads
**`.xlsx` workbooks natively** (ClosedXML — no external PowerShell module) via the `import-excel` verb
and loads them into the **local SQLite working copy** (the same `local.db` the app uses). Mappings are
**authored in the app's Data Import wizard** (Mapping step: map any worksheet to any catalog table —
including runtime tables created in Admin → Model — with header auto-mapping and per-sheet natural
keys) and **saved by name to the shared folder** (`_meta/import-mappings/`); the CLI **only runs saved
mappings** (`import-excel <workbook.xlsx> <mappingName>`, `list-mappings`). Per-catalog normalization,
**FK resolution by natural key**, **expression field-reference resolution** to dictionary entries with
free-text fallback, and idempotent **upsert** producing a reviewable `Import` change set +
`import-report.json`. Importer **configuration** lives in
[`appsettings.json`](src/App.Importer/appsettings.json) (`Importer:DbPath` / `RemoteFolder` /
`ChangedBy`), overridable by `DMS_Importer__*` env vars and explicit arguments; defaults target the
app's own `%LOCALAPPDATA%\MappingStudio` database + shared folder. Run it directly
(`dotnet run --project src/App.Importer -- import-excel working.xlsx "Team workbook"`) or via
[`build/import.ps1`](build/import.ps1); PowerShell scripts (`provision`, `import`, `rebuild-snapshots`,
`convert-format`) back the CLI verbs; Pester tests cover them.

**Publish & sync (`App.Application.Sync` + `App.UI`)** — `SyncCoordinator` ties the local change log,
the remote store and `PublishService` together (tracks the last-published sequence); the `/publish`
page previews the 3-way merge, publishes clean changes, and resolves conflicts via the
`ConflictDialog` (keep mine / theirs / edit); `/history` shows the audit trail; and the
`AutoRefreshService` (`IHostedService`) fast-forwards untouched cells via the new non-logging
`ILocalStore.AdoptCanonical`, never clobbering unpublished edits.

**Mapping Studio + Lineage (`App.UI`)** — the mockup's two signature screens, ported to Blazor over
an editable `MappingStudioState`: the **mappings grid** (rows grouped by target with source chips,
cycling kind/target/type badges, the add bar), the inline **`ExpressionEditor`** (dictionary +
function autocomplete, live syntax highlighting via the Phase-3 engine, unresolved underlines), and
the **lineage view** (`LineageLayout` → SVG graph with upstream-closure highlight, the "Built from"
tree, and the unresolved-reference count). Live at `/mappings` and `/lineage`.

**Entity editors (`App.Application.Provisioning` + `App.Web`)** — a `DefaultCatalog` seed of the seven
normalized entities (principal columns, EN/FR labels, required natural keys); data-driven navigation
tabs; and catalog-driven editors for Applications, Sources, Dictionary, Config, Classification and
Rules (the metadata-driven grid pointed at each table — no per-entity code). Every column header is
click-to-sort and a toolbar **Filters** toggle reveals a per-column filter box (both operate on the
displayed value, so reference/computed columns sort and filter by what the user sees). Verified running.

**UI foundation (`App.UI` + `App.Web` + `App.Desktop`)** — the **design system** in
`_content/App.UI/css/app.css` (Inter + JetBrains Mono, themable `:root` tokens with a light/dark
`data-theme` override, top bar, grid/cells/badges, lineage styles); the
`MsTopBar` shell (brand, tabs, instant EN/FR toggle via `LanguageState`, Publish); and the
cornerstone **`MetadataGrid`** that renders columns + rows from the catalog (inline edit, add-row,
add-column, required validation). The **Blazor Server `App.Web`** host and the **Blazor Hybrid WPF
`App.Desktop`** shell both reuse these components verbatim (the §15 web-port proof).

The web host is **multi-user, multi-host**: each authenticated user gets an **isolated SQLite working
copy + sync coordinator**, provisioned on first use and keyed by their Windows identity (read from the
`AuthenticationStateProvider`, so it works on the live circuit). Writer ids are **host-namespaced**
(`user@host`), so any host can serve any user and several hosts can run against the same shared folder
concurrently without their per-writer logs colliding. Enable authentication with `Auth:Require=true`
(Negotiate); with auth off it collapses to a single OS-account workspace for local dev. The data
directory is configurable via `DataDir` (per-user copies live under `<DataDir>/users/<user>`).

**Engine (`App.Application`)** — the rule/expression engine and lineage engine ported from the
mockup: `ExpressionTokenizer`, `FunctionLibrary` (function/keyword metadata + autocomplete),
`ExpressionClassifier` (syntax-highlight classification), `RuleExpressionBuilder` (text →
dictionary-linked AST with free-text fallback), and `LineageEngine` (field-level graph, upstream
closure, "Built from" tree) over a domain-agnostic `LineageScenario`.

**Remote store & sync (`App.Infrastructure.Remote` + `App.Application.Sync`)** — pluggable
`IRemoteFormat` providers (**Parquet** default, **CSV**, **Excel**) behind `IRemoteFormatProvider`;
per-writer append-only logs (`FileRemoteStore`, atomic appends); the deterministic **fold**
(`ChangeFold`) producing canonical `FoldedState`; materialized **snapshots** with `_meta` sidecars,
incremental refold, and atomic temp+rename (`SnapshotBuilder`); the generic **3-way field-level
merge** (`FieldMergeEngine`) with conflict detection + keep-mine/keep-theirs/edit; the
**`PublishService`** (append-then-refold); and the **`AutoRefreshPlanner`** (fast-forward untouched
cells, flag locally-edited+remotely-changed — never clobbers unpublished edits).

**Domain model (`App.Domain`)** — the column catalog (`ColumnCatalogEntry`, `ColumnKind`,
`CatalogValueType`, SQL-annotation parser `ColumnType`), `app_config`, the rule/mapping expression
AST (`RuleExpression` + nodes, JSON-serialized "tree + original text"), the lineage data model, value
normalization, the seven normalized entities with shared sync/governance stamps, and canonical
`TableNames`. Generic data carriers: `Row`, `ChangeLogEntry`.

**Local store (`App.Infrastructure.Local`)** — a thin generic repository over
`Microsoft.Data.Sqlite` (no EF Core): the catalog (`ICatalog`/`SqliteCatalog`) drives table/column
creation and runtime `ALTER TABLE ADD COLUMN`; the store (`ILocalStore`/`SqliteLocalStore`) reads
the catalog to build parameterized SQL, normalizes values, and emits one change-log entry per changed
field; the append-only change log (`IAuditLog`/`SqliteAuditLog`) **is** the audit trail with a
monotonic `ClientSeq`.

See [RELEASE_NOTES.md](RELEASE_NOTES.md) for the changelog.

## Solution layout

```
src/
  App.Domain/                 entities, column-catalog model, rule AST, lineage model (zero deps)
  App.Application/            services, merge engine, rule parser/evaluator, lineage engine, interfaces
  App.Infrastructure.Local/   Microsoft.Data.Sqlite generic repo, catalog, local change log, audit
  App.Infrastructure.Remote/  per-writer log store + snapshot fold + IRemoteFormat providers
  App.UI/                     shared Razor components (design system, grid, editors, lineage graph)
  App.Desktop/                Blazor Hybrid WPF shell (net10.0-windows)
  App.Web/                    Blazor Server host reusing App.UI (web port + Playwright E2E target)
  App.Importer/               Excel -> DB import CLI (reuses Domain/Application/Infrastructure.Local)
tests/                        one xUnit project per src project + bUnit (UI) + Playwright (E2E)
build/                        PowerShell: provision / import / convert-format / rebuild-snapshots (Phase 9)
reporting/                    Power Query workbook + Power BI .pbit templates (Phase 10)
```

## Tech & conventions

- **.NET 10 (LTS)**; **no EF Core** — a thin generic store over `Microsoft.Data.Sqlite` (the schema
  is metadata-driven, see the column catalog).
- **EN/FR** localization via `.resx` + `IStringLocalizer` (UI strings) and catalog `label_en`/`label_fr`
  (data labels).
- Tests: **xUnit** (unit/integration), **bUnit** (Blazor components), **Playwright** (E2E against
  `App.Web`), **Pester** (PowerShell scripts, from Phase 9).
- Naming note: projects use the `App.*` prefix. Because that makes `App` a namespace, two names were
  adjusted to avoid clashes — the Blazor root component is `AppRoot` (not `App`) and the WPF
  application class is `DesktopApp` (not `App`).

## Getting started

```powershell
dotnet build DataMappingStudio.slnx          # build everything
dotnet test  DataMappingStudio.slnx          # run all tests (browser E2E skips unless DMS_E2E=1)
dotnet run --project src/App.Web             # run the Blazor Server host, then open the shown URL
# dotnet run --project src/App.Desktop       # run the Blazor Hybrid WPF shell (Windows desktop)
```

Browser E2E (Playwright) is opt-in: `pwsh tests/App.E2E.Tests/bin/<cfg>/net10.0/playwright.ps1 install chromium`,
then run with `DMS_E2E=1`. CI does this automatically.

## Roadmap (semver)

Each development phase ships as a minor pre-release; the first feature-complete release is **v1.0.0**.

| Version | Theme |
|---|---|
| v0.1.0 | Solution scaffolding, host bootstrap, EN/FR skeleton, test harness, CI |
| v0.2.0 | Domain core & metadata model |
| v0.3.0 | Local store (generic metadata-driven SQLite) |
| v0.4.0 | Rule/expression engine + lineage engine |
| v0.5.0 | Remote store, fold, merge engine & publish/sync |
| v0.6.0 | UI foundation: shell + design system + dynamic editor |
| v0.7.0 | Entity editor UIs |
| v0.8.0 | Mapping Studio + Lineage UI |
| v0.9.0 | Publish & conflict-resolution UI + sync/audit |
| v0.10.0 | Importer + PowerShell build tooling |
| v1.0.0 | References/computed columns, reporting, retention, web port (feature-complete) |

## License

Released under the [MIT License](LICENSE). Copyright (c) 2026 ardlsoft.com.
