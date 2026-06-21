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
| Current version | **v0.5.0** (Remote store, fold, merge & publish/sync) |
| Build | `dotnet build DataMappingStudio.slnx` — clean |
| Tests | `dotnet test DataMappingStudio.slnx` — 114 passing |

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
dotnet test  DataMappingStudio.slnx          # run all tests
dotnet run --project src/App.Web             # run the Blazor Server host (UI preview, from Phase 5)
```

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
