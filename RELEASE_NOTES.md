# Release Notes

All notable changes to Mapping Studio are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/), and the project uses
[Semantic Versioning](https://semver.org/). Each development phase ships as a minor pre-release
(`0.x.0`); the first feature-complete release will be `1.0.0`.

## [Unreleased]

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
