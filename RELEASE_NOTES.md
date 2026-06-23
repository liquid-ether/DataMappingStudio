# Release Notes

All notable changes to Mapping Studio are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/), and the project uses
[Semantic Versioning](https://semver.org/). Each development phase ships as a minor pre-release
(`0.x.0`); the first feature-complete release will be `1.0.0`.

## [Unreleased]

## [1.3.0] - 2026-06-23

### Changed
- **Mapping Studio & Lineage are now linked to the real data model.** A target's raw sources resolve to
  real `data_source` rows and their fields come from the live `dictionary_entry` columns (new
  `ICatalogQuery`/`CatalogQuery`), instead of free-text alias field lists. `MappingStudioState` enriches
  each source's fields from the dictionary on load (falling back to stored fields when a source has no
  matching data source).
- **Lineage view is now source-scoped.** Pick a source from an autocomplete (over the real data sources);
  the graph is rebuilt for just that source — the targets it feeds, everything downstream, and each
  target's other sources — so the view stays lean with hundreds of sources and redraws on change. Click a
  field to trace its "Built from" path. (`LineageScenario.ScopedToSource`/`SourceNames`.)
- **Grids are virtualized with QuickGrid.** The Mapping grid and the metadata entity editors (e.g. the
  5000-row dictionary) render through `Microsoft.AspNetCore.Components.QuickGrid` with virtualization, so
  large tables stay responsive. The mapping grid trades the per-target group rows/source chips for a flat,
  virtualized table with a Target column (the target filter and search remain).

### Added
- `Microsoft.AspNetCore.Components.QuickGrid` dependency (App.UI).
- Tests: `LineageScenarioScopeTests` (scoping + source list), `MappingStudioLinkTests` (dictionary-backed
  fields + lineage picker), `CatalogQuery` coverage, and a bUnit base context that runs loose JS interop
  and disables grid virtualization so QuickGrid renders fully under test. Suite: **189 passing**.

## [1.2.3] - 2026-06-23

### Added
- **Sample/demo dataset** spanning every table: 10 applications, 100 data sources, 5000 dictionary
  entries (5–150 fields per source), 5 classifications, lookup values, reusable rules, and a Mapping
  Studio model of **30 targets across 5 lineage levels** (the deepest target derives through 5 target
  hops down to raw sources). Deterministic generator `SampleData` (App.Application.Provisioning) +
  `SampleDataSeeder` (App.Infrastructure.Local), which bulk-inserts in one transaction as a canonical
  baseline (no change-log/publish bloat).
- The desktop **auto-seeds** this dataset on a fresh store (empty `application` table, before the
  Mapping Studio demo loads). The web host seeds it only when `SeedSampleData=true` (default off, so
  tests/CI stay deterministic).
- New tooling: `App.Importer seed-sample <dbPath>` and `build/seed-sample-data.ps1` (resets the
  desktop DB, backing it up, then reseeds).
- Tests: generator counts/distribution/lineage-depth/reference-integrity/determinism
  (`SampleDataTests`) and a real-SQLite seed + computed `field_count` check (`SampleDataSeederTests`).
  Suite: **182 passing**.

## [1.2.2] - 2026-06-23

### Fixed
- **Desktop had no menu / could not navigate.** The Blazor Hybrid shell rendered only the
  Applications grid. `DesktopRoot` is now a non-routed view switcher and `MsTopBar` supports
  click-based tab selection (it still renders routed `NavLink`s on the web), so the desktop top
  bar navigates to every editor (Applications, Sources, Dictionary, Config, Classification,
  Rules), the Mapping Studio, Lineage, History and the Publish panel.
- **Desktop showed a blank page with an error bar.** Reverted to standard Blazor Hybrid packaging:
  the WebView host page (`wwwroot\index.html`) ships next to the exe with a relative `HostPage`, so
  BlazorWebView resolves the embedded `_framework`/`_content` assets and the app boots. (The
  earlier single-file approach embedded the host page and pointed `HostPage` at `%LOCALAPPDATA%`,
  which moved the asset root and prevented bootstrap.) The distributable is now
  `MappingStudio.exe` + a 0.8 KB `wwwroot\index.html`.
- Added the missing `#blazor-error-ui` CSS so the error bar stays hidden unless a real unhandled
  error occurs (it was always visible before).

### Added
- **Desktop diagnostics**: a per-launch log file under `%LOCALAPPDATA%\MappingStudio\logs`
  capturing startup, provisioning, and (via the logging pipeline) Blazor component exceptions,
  plus Dispatcher/AppDomain/Task global exception handlers.
- Tests: `DesktopRootTests` now renders the desktop shell with the full service graph and exercises
  **every menu option** (all 6 entity editors, Mapping Studio, Lineage, History, Publish) plus the
  EN/FR toggle; a real-SQLite-store render test guards provisioning. Suite: **174 passing**.

## [1.2.1] - 2026-06-22

### Fixed
- **Desktop startup crash** `System.IO.FileNotFoundException: Could not load file or assembly
  'Microsoft.Windows.SDK.NET'` (thrown from `WebView2CompositionControl.TryInitializeD3DImage`).
  `App.Desktop` now targets the versioned Windows TFM `net10.0-windows10.0.19041.0`
  (min `10.0.17763.0`), which provides the WinRT projection the WebView2 composition control needs.

### Added
- **Compact (framework-dependent) desktop build**: `build/publish-desktop.ps1 -FrameworkDependent`
  produces a ~41 MB single exe that relies on the installed .NET 10 Desktop Runtime (vs ~70 MB
  self-contained). Single-file compression is now applied only to the self-contained build.
- **`build/smoke-desktop.ps1`**: launches the published exe and asserts it survives WPF/WebView2
  startup (catches crashes like the one above); both build flavours verified passing.
- INSTALL.md updated with the two size options, the .NET 10 Desktop Runtime prerequisite, the
  validation step, and troubleshooting for the SDK.NET error.

## [1.2.0] - 2026-06-22

Full Mapping Studio persistence, single-file desktop packaging, and an installation guide.

### Added
- **Target/source (alias) persistence**: new `mapping_target` + `mapping_source` catalog tables and
  `MappingTargetRepository` (`IMappingTargetRepository`) read/write the lineage alias structure to the
  local store; `MappingStudioState` loads it (seeding the demo on first run) so the whole studio model —
  rows *and* targets/sources — is persisted and syncs.
- **Single-file desktop packaging**: `App.Desktop` publishes to one self-contained
  `MappingStudio.exe` (win-x64; .NET + WPF + WebView2 loader + SQLite native bundled; the WebView host
  page is embedded and extracted to `%LOCALAPPDATA%\MappingStudio` at first run). New
  `build/publish-desktop.ps1` (single exe) and `build/publish-web.ps1` (self-contained web host).
- **Configurable shared folder**: the desktop reads `MAPPINGSTUDIO_REMOTE`; the web host reads
  `RemoteFolder` (config/env) — both default to a local per-user folder.
- **[INSTALL.md](INSTALL.md)**: step-by-step install for analysts (desktop), IT (shared OneDrive/
  SharePoint folder), the web host, Excel import, and building from source, with troubleshooting.
- Desktop data now lives under `%LOCALAPPDATA%\MappingStudio` (runs from anywhere, incl. Program Files).
- Tests: target/source persistence round-trip + write-through (170 total). Single-file publish verified.

## [1.1.0] - 2026-06-22

Mapping Studio edits now persist to the local store, so they flow into the change log and publish/sync
(previously the studio was in-memory only).

### Added
- **`MappingRepository`** (`IMappingRepository`, `MappingRecord`) in `App.Application.Mappings`: reads
  and writes Mapping Studio rows to the `mapping` table via `ILocalStore` — every save is an upsert that
  writes change-log entries; delete is a soft delete.
- **`MappingStudioState` is store-backed**: it loads rows from the repository (seeding the demo into the
  store on first run), and `AddRow`/`Save`/`DeleteRow` persist; `MappingRow` gained a stable `Id`.
- The **mappings grid** routes every edit (kind/target/type cycle, field, expression, add, delete)
  through persistence.
- Tests: repository round-trip/update/delete, studio seed + cross-instance persistence + write-through
  (bUnit/unit), and an end-to-end **mapping edit → pending → publish** integration test. 169 passing.

### Changed
- The `mapping` catalog table now stores the studio's flat shape (`target`, `field`, `kind`, `type`,
  `expression`, `is_tokenized`, `notes`); the importer mapping no longer maps `kind`.

## [1.0.0] - 2026-06-22

Feature-complete: the Phase-2 architecture features — reference & computed columns, opt-in reporting
views, and log retention — completing both delivery phases.

### Added
- **Reference-typed columns**: `ReferenceService` (`IReferenceResolver`) lists options and resolves
  stored ids to live display values; the metadata grid renders reference columns as **pickers** (and
  computed columns read-only). The default catalog now marks the real FKs as references
  (`data_source.application_id`, `dictionary_entry.source_id`/`classification_id`).
- **Computed columns** (`IComputedEvaluator`): catalog `Formula` evaluated on read — `count(table.fk)`
  (e.g. `data_source.field_count`) and `lookup(ref.col)` **autofill** (e.g. dictionary
  `access_901`/`disclosure_902` from the referenced classification).
- **Opt-in reporting views** (`ReportingViewBuilder`): flat `<table>_report.<ext>` with references
  resolved + computed evaluated; `reporting/MappingStudio.pq` Power Query template + guide for Power BI.
- **Log compaction/archiving** (`LogCompactor`): rolls change-log entries older than a threshold into
  dated per-writer archive files, keeping recent entries live.
- **Importer CLI** `report` and `compact` verbs.
- Tests: reference options/display + computed count/lookup + grid picker rendering (bUnit), reporting
  view flattening + compaction (integration). 163 passing (+4 E2E skipped). Pages re-verified live.

### Notes
- Web-port hardening: `App.Web` already reuses `App.UI` verbatim and is the verified web target; no
  further fork is required (Architecture §15).
- Lookup-by-type references (status/type/frequency → `lookup_value` filtered by `lookup_type`) remain
  scalar text for now; the catalog already supports promoting them to references without migration.

## [0.10.0] - 2026-06-22

The Excel importer and PowerShell build tooling — the migration path off the current workbook.

### Added
- **`App.Importer` pipeline** (`Importer`, `ImportMapping`, `ImportReport`): for each worksheet, map
  only the configured columns (derived/computed omitted; unmapped headers reported), normalize per the
  catalog, **resolve FK references by natural key**, **resolve expression field-references** to
  dictionary entries (free-text fallback), and **upsert by natural key** as one reviewable
  `Import` change set; bad rows are skipped + reported, never aborting the run.
- **`build/import-mapping.json`** — the editable worksheet→table mapping (columns, naturalKey,
  references, expressionColumn) for the seven entities, with the `data_source → application` FK by
  `app_code`.
- **CLI verbs** in `App.Importer` (`provision`, `import`, `rebuild-snapshots`, `convert-format`) and
  matching **PowerShell scripts** in `build/` (`import.ps1` exports each worksheet via the `ImportExcel`
  module to JSON, then invokes the importer).
- **`ILocalStore.Upsert`** gained an optional operation override so imports tag entries `Import`.
- Tests: 6 xUnit importer tests (column mapping + unmapped reporting, re-run upsert idempotency, FK
  resolve + unresolved reporting, required-field skip, expression resolve + free-text fallback) and
  Pester tests (`tests/Build.Tests`) for the scripts + mapping. CLI import verified end-to-end.
  157 passing (+4 E2E skipped).

## [0.9.0] - 2026-06-22

The publish / conflict-resolution UI and the sync + audit machinery — the full edit → publish →
resolve → audit loop.

### Added
- **`ILocalStore.AdoptCanonical`** — writes remote-pulled canonical values locally **without** a
  change-log entry (the non-logging path auto-refresh needs); deferred from v0.5.0, now implemented in
  `SqliteLocalStore`.
- **`SyncCoordinator`** (`ISyncCoordinator`) — orchestrates preview / publish / refresh / history over
  the local change log + remote store + `PublishService`, tracking the last-published sequence so
  "pending" means newer local edits.
- **`AutoRefreshService`** (`IHostedService`) — periodic non-disruptive fast-forward of untouched cells
  (flags locally-edited+remotely-changed for review); wired via `AddRemoteStore(..., enableAutoRefresh)`.
- **UI**: `PublishPanel` (`/publish`) previews the merge, publishes clean changes, and shows the
  `ConflictDialog` (per-field keep mine / keep theirs / edit) when needed; `HistoryView` (`/history`)
  renders the audit trail; the top-bar Publish button navigates to `/publish`; a History nav tab.
- Tests: bUnit (publish reports count, conflict dialog flow, refresh status, keep-theirs resolution)
  and an end-to-end `SyncCoordinator` integration test across the real local + remote stores
  (publish-clean / conflict-block-then-resolve / refresh-fast-forward) + a gated Playwright publish/
  history flow. 151 passing (+4 E2E skipped). Pages verified live.

## [0.8.0] - 2026-06-22

The Mapping Studio and Lineage screens — a faithful Blazor port of the approved mockup.

### Added
- **`MappingStudioState`** (`App.UI.MappingStudio`): in-memory editable model (targets + sources +
  rows) seeded with the mockup demo; builds a `LineageScenario` on demand to drive the engines.
- **`ExpressionView`** — read-only syntax highlighting (functions, keywords, literals, field chips by
  source colour, unresolved underline) via the Phase-3 `ExpressionClassifier`.
- **`ExpressionEditor`** — click-to-edit with dictionary + function **autocomplete** (filtered by the
  trailing token, arrow/enter/escape keys, click-to-insert) and live highlighting; commits back to the row.
- **`MappingGrid`** — the mappings grid: rows grouped by target with source chips, cycling
  kind/target/type badges, inline field + expression editing, search + target filter, and the
  +Field/+Calc/+Join/+Filter add bar.
- **`LineageLayout`** + **`LineageView`** — the SVG lineage graph (level columns by dataset depth,
  cubic-bezier edges, upstream-closure highlight + dimming), the "Built from" tree, and the
  unresolved-reference count, driven by the Phase-3 `LineageEngine`. SVG coords are invariant-formatted
  so they survive the FR culture toggle.
- Pages `/mappings` and `/lineage` with nav tabs; `MappingStudioState` registered scoped in `AddAppUi`.
- Tests: 6 bUnit (expression highlight, grid grouping + chips + add-row, lineage closure + unresolved,
  autocomplete) + a gated Playwright studio flow. 144 passing (+3 E2E skipped). Pages re-verified live.

### Notes
- The studio edits an in-memory model (as the mockup did); persisting rows to the `mapping` table and
  binding sources/aliases to stored data is a later refinement.

## [0.7.0] - 2026-06-21

The entity editor UIs — every core entity is now CRUD-able through metadata-driven editors.

### Added
- **`DefaultCatalog`** (`App.Application.Provisioning`): the default column catalog for the seven
  normalized entities (application, data_source, dictionary_entry, lookup_value, classification, rule,
  mapping) — principal columns with EN/FR labels and required natural keys — plus a `Navigation` list
  of the editable entities. Reference/lookup columns are seeded as scalar text for now (Phase 2 /
  v1.0.0 upgrades them to reference pickers; the importer reconciles the full per-field catalog).
- **Data-driven navigation**: `MainLayout` builds the top-bar tabs from `DefaultCatalog.Navigation`;
  `Home` lists the entities; the catalog-driven `/t/{table}` page titles each entity in EN/FR.
- **Entity editors** for Applications, Sources, Dictionary, Config, Classification and Rules — the
  `MetadataGrid` pointed at each entity's table (no per-entity code; new columns appear automatically).
- The web host now provisions `DefaultCatalog` (replacing the demo table); the desktop shell opens the
  Applications editor.
- Tests: `DefaultCatalogTests` (entities present, required natural keys, all-valid + bilingual,
  navigation list) and bUnit `EntityEditorTests` (Applications required `app_code`, Dictionary columns,
  FR label toggle); a gated Playwright entity-flow E2E (create an Application, persists across reload).
  138 passing (+1 E2E skipped locally). Web host re-verified (Applications editor renders).

## [0.6.0] - 2026-06-21

The UI foundation — the mockup design system, the app shell, and the metadata-driven editor, hosted
by both Blazor Server and Blazor Hybrid (WPF).

### Added
- **Design system** (`App.UI/wwwroot/css/app.css`) ported from the approved mockup (CSS tokens, IBM
  Plex fonts, dark top bar, toolbar/search, grid + cells + kind/type badges, expression chips,
  autocomplete, and the lineage SVG styles for Phase 7), served as an RCL static asset.
- **`LanguageState`** — instant EN/FR toggle (mirrors the mockup i18n) plus a bilingual catalog-label
  helper; registered in `AddAppUi`.
- **`MsTopBar`** shell component — brand glyph, navigation tabs, EN/FR toggle, Publish.
- **`MetadataGrid`** — the cornerstone metadata-driven editor: renders columns + rows from the column
  catalog (so a new column appears with no code change), with inline editing, add-row, runtime
  add-column, and required-field validation.
- **`App.Web`** Blazor Server host fully wired (Generic Host DI: Application + Local + Remote + UI,
  catalog seed + `EnsureSchema` provisioning, interactive render mode, fonts/CSS), with a `Home` page
  and a generic `/t/{table}` grid page. Verified running (HTTP 200, shell + grid render).
- **`App.Desktop`** Blazor Hybrid WPF shell — `BlazorWebView` hosting the shared `DesktopRoot`
  component with its own DI + provisioning.
- **bUnit** tests (7 new): top bar render + EN/FR toggle + publish callback; grid headers/required
  marker, runtime add-column, add-row persistence, required-field block. **Playwright** E2E
  (`WebHostFixture` launches real Kestrel; smoke flow: load → toggle FR → navigate to grid), gated by
  `DMS_E2E=1` and wired into CI (`playwright install chromium`). 121 passing + 1 E2E skipped locally.

### Changed
- `App.Desktop` now boots via `OnStartup` DI composition (no `StartupUri`); CI gained a Playwright
  E2E step.

## [0.5.0] - 2026-06-21

The remote store, deterministic fold, field-level merge and publish/sync — the git-like sharing layer.

### Added
- **Pluggable remote formats** (`IRemoteFormat` + `IRemoteFormatProvider`): **Parquet** (default,
  Parquet.Net untyped serializer), **CSV** (RFC 4180-ish, zero-dependency), **Excel** (ClosedXML);
  `RemoteValueConverter` maps canonical strings ↔ native typed columns. `RemoteTable`/`RemoteColumn`
  carriers in the domain.
- **Per-writer append-only logs** (`FileRemoteStore`): `_changes/<writer>.<ext>`, atomic appends,
  read-all/read-writer/writers; `ChangeLogSchema` maps entries ↔ tabular rows. `AtomicWrite` helper.
- **Deterministic fold** (`ChangeFold` → `FoldedState`/`FoldedRow`): stable order (ChangedAtUtc, then
  ChangeId), last-write-per-cell, delete-aware; incremental `Apply` on a seed.
- **Snapshot builder** (`SnapshotBuilder`): materialized `<table>.<ext>` + `_meta/<table>.json`
  per-writer ClientSeq sidecars, incremental refold from the existing snapshot, atomic temp+rename.
- **3-way field-level merge** (`FieldMergeEngine`): clean vs converged vs conflict, with
  keep-mine / keep-theirs / edit resolution; `PublishService` (append-then-refold, snapshot rebuild).
- **Auto-refresh planner** (`AutoRefreshPlanner`): fast-forwards remote changes for untouched cells,
  flags locally-edited+remotely-changed cells — never clobbering unpublished edits.
- 27 new tests (114 total): format round-trips (incl. nulls/empty/quoting), multi-writer logs, fold
  determinism, snapshot full-vs-incremental + atomicity + delete exclusion, merge + resolution,
  publish (clean / conflict-blocked / resolved), auto-refresh.

### Notes
- The auto-refresh `IHostedService` wrapper (timer that applies the planner to the local store) is
  deferred to the sync UI phase (v0.9.0): applying a fast-forward needs a non-logging "adopt
  canonical" path on the local store. The planner logic + its guarantees are complete and tested.

## [0.4.0] - 2026-06-21

The rule/expression engine and lineage engine — the logic behind Mapping Studio, ported from the
approved mockup and tested against its exact expressions and lineage results.

### Added
- **`ExpressionTokenizer`** — faithful port of the mockup's tokenizer regex (plus numeric literals,
  which the mockup regex silently dropped).
- **`FunctionLibrary`** — predefined functions and keywords as metadata (`FunctionDefinition` with
  signature + EN/FR descriptions); `IsFunction`/`IsKeyword`/`Matching` for completion and validation.
- **`ExpressionClassifier`** — classifies tokens for highlighting (functions, keywords, literals,
  known field chips with colour class, unresolved `alias.field`), port of `renderExpr`; unresolved
  counting.
- **`ResolutionContext`/`KnownReference`** — the scope of references available to an expression.
- **`RuleExpressionBuilder`** — builds a stored `RuleExpression` (dictionary-linked field references,
  unresolved kept unlinked-and-flagged, blank/free-form → `RawTextNode`); `FunctionsUsed`.
- **`LineageEngine`** over a domain-agnostic **`LineageScenario`** — field-level graph build
  (`buildLineage` port), upstream highlight closure, and the "Built from" tree, cycle-guarded.
- 14 tests using the mockup scenario verbatim (incl. the `MARKETING_SEGMENTS.priority → CUSTOMER_360
  → BILLING.mrr` chain and the single unresolved `b.plan_label`). 87 total.

### Changed
- `RuleExpression.HasUnresolved` now flags only a whole-expression raw-text root or unresolved field
  references (inner operator/punctuation raw fragments no longer count).

## [0.3.0] - 2026-06-21

The generic, metadata-driven local SQLite store — the working copy every analyst edits. No EF Core.

### Added
- **Generic data carriers** in `App.Domain`: `Row` (catalog-shaped, indexer-bound, canonical string
  values) and `ChangeLogEntry`/`ChangeOperation` (the audit tuple).
- **Application abstractions**: `ICatalog`, `ILocalStore`, `IAuditLog` (interfaces live in
  `App.Application` per the layered architecture).
- **`SqliteCatalog`** (`column_catalog` meta table): seed (idempotent), read by table/all, and
  runtime `AddColumn` that records the column and applies `ALTER TABLE … ADD COLUMN` so it appears in
  editors with no code change; rejects invalid entries.
- **`SqliteLocalStore`**: catalog-driven `CREATE TABLE`/`ALTER TABLE`, dynamic reads into `Row`,
  value normalization on write, diff-driven upsert that writes one change-log entry per changed field
  (Insert/Update ops), row-version bump, and soft delete (Delete op, hidden from default reads).
- **`SqliteAuditLog`** (`local_change_log` meta table): append-only change log = audit trail, with a
  monotonic per-writer `ClientSeq`; query by table/row and "pending since seq".
- **`LocalDatabase`** connection holder (WAL, FK on) and safe dynamic-SQL helpers
  (`SqliteExtensions`, `SqlIdentifier` whitelist+quoting); DI `AddLocalStore(path)`.
- 16 integration tests against real temp-file SQLite (round-trip, per-field change logging,
  row-version, normalization, unknown-column rejection, soft delete, monotonic ClientSeq, runtime
  add-column, DI wiring with ValidateOnBuild). 75 total.

### Security
- Bumped `SQLitePCLRaw.bundle_e_sqlite3` to 3.0.3 to clear advisory GHSA-2m69-gcr7-jv3q.

## [0.2.0] - 2026-06-21

The domain core & metadata model — the foundation every later layer builds on. Zero external
dependencies.

### Added
- **Column catalog model**: `ColumnCatalogEntry` (table/column, kind, value type, EN/FR labels,
  required/core/user-added flags, reference target, computed formula, display order) with invariant
  validation; `ColumnKind` (scalar/reference/computed) and `CatalogValueType` enums; `ColumnType`
  parser mapping the workbook's SQL-style annotations (`VARCHAR(n)`, `VARCHAR(MAX)`, `UUID`,
  `UniqueID`, `INT`, `Boolean`, `TIMESTAMP`, …) to canonical types.
- **`app_config`** model (`AppConfigEntry`, `ConfigScope` local/shared).
- **Rule/mapping expression AST** ("exploitable format"): polymorphic, JSON-serialized
  `RuleExpression` (tree + original text) with `LiteralNode`, `FieldReferenceNode`
  (dictionary-bound, rename-safe), `FunctionCallNode` (also operators/SQL constructs) and
  `RawTextNode` free-text fallback; helpers for field-reference enumeration and unresolved detection.
- **Lineage data model**: `LineageNode`/`LineageEdge`/`LineageGraph` keyed as `dataset§field`.
- **Value normalization** (`ValueNormalizer`): culture-invariant canonical forms per type (incl.
  French decimal comma) so imports and edits compare without spurious conflicts.
- **Seven normalized entities** (`ApplicationEntity`, `DataSource`, `DictionaryEntry`, `Rule`,
  `Mapping`, `LookupValue`, `Classification`) with shared `SyncMetadata`/`GovernanceMetadata`
  stamps, plus canonical `TableNames` (FK dependency order).
- 52 new unit tests covering annotation parsing, normalization, AST round-trips, catalog invariants
  and entity model rules (61 total).

## [0.1.0] - 2026-06-21

Initial solution scaffolding and test harness (foundation for all later work).

### Added
- Solution `DataMappingStudio.slnx` with the full project layout under `src/` and `tests/`:
  `App.Domain`, `App.Application`, `App.Infrastructure.Local`, `App.Infrastructure.Remote`,
  `App.UI`, `App.Desktop` (WPF/Blazor Hybrid shell), `App.Web` (Blazor Server host), `App.Importer`.
- Composition seams: `AddApplication`, `AddLocalStore`, `AddRemoteStore`, `AddAppUi` DI extensions
  and an `IClock`/`SystemClock` abstraction used by future timestamping.
- Shared **.NET Generic Host** bootstrap for the importer (`ImporterHost`).
- **EN/FR** localization skeleton: `SharedResource` + `Resources/Localization/SharedResource.{,.fr}.resx`
  with the first UI string keys mirrored from the mockup.
- Test projects (one per production project) using **xUnit**, plus **bUnit** (`App.UI.Tests`) and
  **Microsoft.Playwright** (`App.E2E.Tests`); 9 smoke tests covering DI resolution, host
  composition, component rendering, and EN/FR string lookup.
- `Directory.Build.props` (repo-wide C#/nullable/analyzer defaults), `global.json` (SDK pin to
  10.0.x), `.gitignore`, and a GitHub Actions **CI** workflow (build + test with coverage on a
  Windows runner; Pester step ready for Phase 9).

### Changed
- Blazor root component renamed `App` -> `AppRoot` and WPF application class renamed `App` ->
  `DesktopApp` to avoid clashes with the `App.*` root namespace in generated code.
