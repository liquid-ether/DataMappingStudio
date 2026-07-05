# Data Mapping & Lineage App — Project Reference

> Canonical reference for the project. Captures the original description, the agreed
> decisions, and the proposed architecture. Living document — update as decisions change.
> Last revised: Rules confirmed first-class; added companion Diagrams file (ER / workflow / publish);
> normalization pass (technical identifiers + FR/EN labels, Config split, FK-not-copy); §18 upgrade
> candidates under review.

---

## 1. Purpose

A **Blazor Hybrid** desktop application for **data mapping and lineage** that runs entirely in
the user's context (no elevation, no admin). It is an **upgrade of the team's current Excel
workbook**, which has become slow and conflict-prone with multiple concurrent editors.

The app is a **metadata tool, not a data tool**: it captures *specifications* — what sources
exist, what their fields are, and how target fields are derived (joins, filters, direct
mappings, calculations) — never the underlying data itself. Its output (the published mapping
+ rule catalog) is consumed downstream by an **ETL engine**, which is the system that actually
moves data and applies classification/tokenization at execution time. This scoping keeps the
app free of any data-sensitivity handling (§4, §18 Q-U4 — withdrawn).

About **10 analysts** edit in parallel across different computers, each publishing roughly
**3–4 times a day**, and share the same metadata through a **git-like sync with field-level
merge/conflict resolution**. Each analyst works on their own local copy and reconciles with a
shared canonical copy via an explicit **Publish** action, so no analyst is ever blocked by
another.

---

## 2. Original project description (as provided)

Create an app that runs in the Windows user context and can sync its data so that multiple
instances on different computers share the same data through sync + merge conflict resolution,
akin to git.

The app is a data mapping and lineage app with the following entities:

- **Application repository**
- **Data Source repository** (belongs to an Application)
- **Data Dictionary** (linked to a Data Source)
- **Rules repository** (linked to a Data Dictionary; rules stored in an exploitable
  i.e. machine-processable format)
- **Mapping repository** (maps between a source A and a source X using rules and direct
  dictionary mapping)
- **Config repository** (handles application configuration)

---

## 3. Constraints & decisions

| # | Topic | Decision |
|---|-------|----------|
| 1 | Environment | Windows, **user context only**, no elevation of privileges |
| 2 | Runtime | **.NET 10 (LTS)**, minimum external libraries (relaxed where needed for format support) |
| 3 | Desktop UI | **Blazor Hybrid** (was WPF) — chosen for the web feel and to ease the eventual web port; see §5a |
| 4 | Sharing layer | Files in a **SharePoint** document library, reached as a **OneDrive-synced local folder** (no app registration, no admin) |
| 5 | Local store | **Single SQLite DB** (cross-application mappings must be co-located) |
| 6 | Remote store | **Per-writer append-only change logs** (was per-table snapshot files) — adopted given ~10 concurrent analysts, 3–4 publishes/day each; see §6/§7 |
| 7 | Remote format | **Parquet (default)**, **config-switchable to CSV or Excel**; all required libraries bundled. Must be read-only consumable by Excel Power Query + Power BI with no driver install |
| 8 | Data access | **No EF Core** — thin generic store over `Microsoft.Data.Sqlite` (more control; required by dynamic schema) |
| 9 | Conflict granularity | **Per field, on the same record** |
| 10 | Audit | **Full audit trail** (who/when/old/new); the change-log fold *is* the audit log (§8) |
| 11 | Freshness | **Auto-refresh** pulls from remote on an interval |
| 12 | Dynamic schema | Analysts can **add columns**; they appear **automatically in UI editors**. Column types: **scalar + reference**, with **scalar delivered first, references second** |
| 13 | Rules | **First-class** entity; predefined function library **plus** free-text; **autocompletion of dictionary entries** with each referenced field linked to its entry; fast entry |
| 14 | Reporting | Remote snapshots used **read-only**; **denormalized reporting views generated at publish time** (opt-in), so a table's lookups + computed fields export flat in one pass — see §11 |
| 15 | Import | **Importer tool** using the existing **ImportExcel** PowerShell module |
| 16 | Localization | **English + French** |
| 17 | Future | Easily convertible to a **multi-tier web app** — Blazor Hybrid materially eases this (§5a) |
| 18 | Tooling | **PowerShell** for build/CLI scripts |
| 19 | Data sensitivity | **Out of scope.** App is metadata-only; classification/tokenization is applied by the downstream **ETL engine**, not this app — see §1, §18 (Q-U4 withdrawn) |

Field lists per entity are now defined from `TableDefinition.xlsx` (Appendix A). Still pending: a
small sample **data** extract (to validate import mapping and natural keys) and rule-editor UI
sign-off.

---

## 4. Metadata-driven data model (central design choice)

"Add columns and have them appear automatically in the UI" means the schema cannot be
hard-coded. The whole data layer is **metadata-driven and generic**:

- A **Column Catalog** describes every table's columns: `(TableName, ColumnName, DataType,
  LabelEn, LabelFr, IsRequired, IsReference, ReferenceTarget?, DefaultValue, DisplayOrder,
  IsUserAdded)`. The reference fields are reserved from the start even though reference
  columns are implemented in the second build phase (§17).
- SQLite tables are **wide**; user-added columns are real columns added via
  `ALTER TABLE ... ADD COLUMN`, recorded in the catalog.
- **UI editors render from the catalog** — a Razor grid/form component that generates
  columns/fields from metadata, so a new column appears with no code change. Labels come from
  EN/FR catalog entries.
- The **merge engine and audit log are generic**, operating on `(Table, RowId, Column, Value)`
  tuples — exactly the field-level granularity required.

**No EF Core (firm).** Fixed EF POCOs fight runtime column changes; a thin **generic
repository over `Microsoft.Data.Sqlite`** reads the catalog and builds parameterized SQL, with
rows as a dynamic `Row : ICustomTypeDescriptor` (or indexer-bound `Row[Column]`) so the Razor
grid component can bind to them generically. Gives more control and trims dependencies.

### Column types (phased)
- **Scalar** (text / number / date / bool) — **Phase 1**.
- **Reference** (value links to a row in another table; participates in lineage) — **Phase 2**.
  The catalog schema and merge/audit model already carry reference metadata so Phase 2 needs no
  migration. The workbook's `…→ Config`/`…→ Classification` lookups and `Système/Code applicatif`
  **autofills** are reference columns; the autofill display values are resolved live, not stored
  (avoiding the Excel's denormalization drift).
- **Computed** (value derived from a stored formula via the rule engine — §7b). The workbook needs
  these on day one: `Source.NB Fields` (count of dictionary entries), `Dictionnary.UniqueKey`
  (`Source.Colonne`), and the autofill-on-select behaviours. The catalog carries an optional
  `Formula` per column; computed columns are read-only in editors and recomputed on change.
  Treated as **Phase 2** alongside references (Phase 1 may seed them as plain values).

### Fixed core vs user-added
Each table has **core columns** the app relies on (IDs, FKs, sync metadata, not user-editable)
plus any number of **user-added columns**. The catalog marks which is which.

### Entities (from `TableDefinition.xlsx`)
Full field definitions are in **Appendix A**. The catalog is seeded from that workbook. All rows
also carry sync metadata `(RowVersion, LastModifiedUtc, LastModifiedBy)` — `RowVersion` is a
local edit counter for fast unchanged-row skips; conflict detection itself is per-cell against
the analyst's last-known fold, not against a table-level version (§6a/§7a).

- **Apps** (Application) — `Id` (UUID) + AppCode, the GDM/VA360/short names, the Gold directory,
  description, and the four TI/Business responsible-party fields.
- **Source** (DataSource) — `ID Source` (UUID), `IdApp` → Apps, plus ingestion/bronze/standardised/
  gold directories, type/bloc/frequency/status/data-product (all **lookups into Config**), revision
  governance, and `NB Fields` (**computed**: count of its dictionary entries).
- **Dictionnary** (DataDictionary / DictionaryEntry) — `Id`, `Source ID` → Source, `Colonne`, data
  type, key flags (`cle sk`, `cle pk`), nullable, business name, FSDF field, language, and the
  privacy block (`PRP-ID` → Classification, plus auto-filled Access-901 / Disclosure-902 and
  tokenisation). `UniqueKey` is **computed** (`Source.Colonne`).
- **Mapping** — kept in **our expression model**, not the Excel's denormalized shape. We keep only
  the core fields (target dictionary entry, the transformation expression, rule/element refs,
  tokenisation flags, governance, notes); every "shown"/"used" column in the sheet
  (`Sources utilisées`, `Tous les champs utilisés`, `Source - colonne/système/fichier`, etc.) is
  **derived by the expression + lineage engine**, never stored. This is precisely why ours is less
  conflict-prone — no redundant denormalized columns to drift. See Appendix A for the field mapping.
- **Rule** — named, reusable expressions (§7b). The sheet encodes rule references inside Mapping
  (`ID champ ou regle`); our model promotes them to first-class reusable expressions.
- **Config** — `Id, Value, DescriptionFR, DescriptionEN, Type`. A **typed, bilingual lookup table**:
  `Type` partitions it (SourceType, Status, Frequency, BlocDefinition, DataProduct, Langue,
  Tokenisation, …) and most reference columns above point into it. Note this means **data**, not just
  UI, is bilingual (FR/EN descriptions) — complementing §12.
- **Classification** *(new entity)* — `Id, ClassificationPRP, ClassificationAcces,
  ClassificationDivulgation`. Privacy/access reference table that Dictionnary entries point at.
  **Pass-through metadata only**: the app stores and displays it (so the catalog stays a complete
  specification for the downstream ETL engine) but does not interpret, enforce, or mask anything
  based on it — see §1 and §18 (Q-U4 withdrawn).
- **AuditLog (`logs`)** — §8.

These confirm two model needs: heavy use of **reference columns** (Phase 2) — Config and
Classification are reference targets — and **computed columns** (see below).

> **Diagrams:** the normalized ER model, the user workflow, and the publish/merge flow are in
> [`DataMappingLineageApp-Diagrams.md`](DataMappingLineageApp-Diagrams.md).

### Normalization pass (naming & references)

The workbook mixes French/English identifiers, denormalized copies, and inconsistent types. The
normalized model the ER diagram reflects applies five principles:

1. **Technical identifiers, labels in the catalog.** Physical names are stable, language-neutral
   snake_case (`Nom Affaire → business_name`, `Répertoire Brute → bronze_path`); the French/business
   wording moves into `label_fr`/`label_en`. Fixes mixed-language names and the `Dictionnary` typo.
2. **Canonical types.** `uuid · text · int · bool · timestamp`, with max-length a catalog attribute.
   Unifies `UUID`/`UniqueID` and `VARCHAR(n)`/`VARCHAR(MAX)`.
3. **Store references, derive projections.** Every Excel *lookup/autofill* becomes either a stored
   **FK** (reference column) or a **derived/computed** projection — never a stored denormalized copy.
   So `Source.Système`, `Dictionnary.Source`, `Access-901`/`Disclosure-902`, and all the Mapping
   "used/shown" columns are computed, not stored. This removes the update anomalies that make the
   current Excel drift.
4. **Config split** (design question Q-N1): the overloaded `Config` becomes `lookup_value` (typed
   bilingual enumerations) **+** `app_config` (runtime settings, `local`/`shared` scope).
5. **Uniform stamps.** Every domain row gets governance `(status_lookup_id, revised_at,
   revision_ref, notes)` and sync `(row_version, base_version, modified_at, modified_by, is_deleted)`,
   named identically everywhere.

**Normalized tables:** `application, data_source, dictionary_entry, rule, mapping, lookup_value,
classification, audit_log` + meta `column_catalog, app_config`.

**Name map (Excel → normalized):** `Apps→application`, `Source→data_source`,
`Dictionnary→dictionary_entry`, `Mapping→mapping` (our expression model), `Config→lookup_value (+
app_config)`, `Classification→classification`, `logs→audit_log`. Per-field technical names are
assigned from Appendix A on the same basis (label_fr keeps the original wording).

**Open normalization questions:**
- **Q-N1** Confirm the `Config → lookup_value + app_config` split.
- **Q-N2** Confirm technical-identifier naming with FR/EN labels (vs keeping French business names as
  physical columns — some teams prefer the physical schema to match business vocabulary).
- **Q-N3** Keep the four responsible-party fields and the bronze/silver/gold paths **inline** (1:1),
  or normalize into child tables (`app_contact(role,person)`, `source_path(zone,path)`) for flexibility?

---

## 5. Architecture — layers

```
+---------------------------------------------------------------+
|  Presentation — Blazor Hybrid (BlazorWebView in WPF/MAUI shell)|
|  - same Razor components reused by the future Blazor Web app   |
|  - metadata-driven dynamic editors                             |
+---------------------------------------------------------------+
|  Application  (use cases, transport-agnostic)                 |
|  - Entity services, IPublishService, ISyncService             |
|  - Generic field-level merge engine                           |
|  - Rule parser / evaluator                                    |
|  - Interfaces: ILocalStore, IRemoteStore, IRemoteFormat,      |
|    IAuditLog, ICatalog                                        |
+---------------------------------------------------------------+
|  Domain  (entities, rule AST, catalog model — zero deps)      |
+---------------------------------------------------------------+
|  Infrastructure.Local             |  Infrastructure.Remote    |
|  - Microsoft.Data.Sqlite (single  |  - per-writer change logs |
|    DB, generic repo, catalog)     |    + materialized table   |
|                                   |    snapshots (§6)         |
+---------------------------------------------------------------+
```

Bootstrapped with the **.NET Generic Host** so DI/config/logging and the background refresh
service mirror ASP.NET Core. Domain + Application + merge/rule engines are reused verbatim by
the future web tier (§15).

### 5a. Why Blazor Hybrid (not WPF)

Given the lean toward a **web feel** to ease the eventual web port, **Blazor Hybrid** replaces
WPF as the desktop shell:

- **One component tree, two hosts.** Razor components (the metadata-driven editors, the
  Excel-like grid, the lineage graph) are written once and rendered by a `BlazorWebView` inside
  a thin native shell today, and by the **same components** in a server-hosted Blazor Web App
  later. The web conversion (§15) becomes "swap the host," not "rewrite the UI."
- **Same C# end to end** — no JS framework, no separate web designer skillset; the team keeps
  one language across Domain/Application/UI.
- **Trade-off, explicit:** Blazor Hybrid renders in an embedded WebView2, so it inherits a *web*
  look/interaction model rather than native Windows chrome (no Fluent/WinUI controls,
  drag-and-drop and clipboard need WebView2 bridging, startup is marginally heavier than WPF).
  Given the stated preference, this is judged the right trade.
- **Still zero-admin, zero-install-beyond-the-app**: WebView2 ships in Windows 10/11 by default
  (evergreen, pre-installed on current Windows builds), so no separate runtime install is
  required in the target environment.
- Library footprint stays minimal: `Microsoft.AspNetCore.Components.WebView.Wpf` (or `.Maui`) is
  the only addition versus the prior WPF plan; everything else in §13 is unchanged.

### Suggested solution layout
```
src/
  App.Domain/                      (entities, catalog model, rule AST)
  App.Application/                 (services, merge engine, rule engine, interfaces)
  App.Infrastructure.Local/        (Microsoft.Data.Sqlite generic repo, catalog, audit)
  App.Infrastructure.Remote/       (per-writer log store + snapshot builder + format providers)
  App.UI/                          (shared Razor components — grid, editors, lineage graph)
  App.Desktop/                     (Blazor Hybrid shell, thin — single-user, local working copy)
  App.Web/                         (ASP.NET Core Blazor Server host — per-user workspaces; see §15)
  App.Importer/                    (Excel → DB import CLI; pairs with PowerShell)
build/                             (PowerShell publish/provision/import/convert scripts)
reporting/                         (Power Query workbook + Power BI .pbit templates)
tests/
```

---

## 6. Persistence, sync topology & remote format

**Local working copy:** one **SQLite** DB per analyst — all tables + column catalog + local
change log + sync metadata. Cross-application mappings work because everything is co-located.

### 6a. Remote store — per-writer append-only change logs (revised from per-table snapshots)

The original design held **one canonical file per table**; with ~10 analysts publishing 3–4×/day
each, multiple writers regularly target the *same* table file, so OneDrive conflict copies would
be a routine occurrence rather than an edge case. The revised design removes that contention
structurally:

**Canonical folder layout — confirmed flat.** A single synced folder holds `_changes/`, `_meta/`,
and the materialized snapshots (no per-Application subfolder). The per-writer model already
removes the collision concern a subfolder split would have addressed, so flat is simplest; revisit
only if folder-level SharePoint permissions per Application become a requirement.

- Each analyst owns **one append-only log file**: `_changes/<analyst-id>.log` (Parquet/CSV/Excel
  per the configured format, same as before). **Only that analyst ever writes that file** —
  publishing means *appending* rows to a file nobody else touches, so two analysts publishing
  simultaneously physically cannot collide. OneDrive conflict copies on `_changes/*` become
  structurally near-impossible rather than something the merge engine has to reconcile after
  the fact. (On the multi-host web tier the writer id is **host-namespaced**, `<user>@<host>`, so
  the "only the owning writer writes their file" invariant holds per *(user, host)* pair even when
  several hosts serve the same user — see §15, affinity model B.)
- A log row is one published field change: `(ChangeId, ChangeSetId, Table, RowId, Column,
  OldValue, NewValue, Operation, ChangedBy, ChangedAtUtc, ClientSeq)`. This **is** the audit log
  — see revised §8.
- **Canonical current state** for a table is the **deterministic fold** of every analyst's log,
  replayed in a stable order (by `ChangedAtUtc` then `ChangeId` as tiebreak) up to "last write
  per cell wins" — i.e. the same diff3-derived per-cell resolution as before, just computed by
  folding logs instead of diffing two files. Determinism means any machine folding the same set
  of logs gets the same canonical state, with no coordination required.
- **Materialized table snapshots** (`<table>.parquet` etc.) are still produced — they're a
  **read-optimization, not the source of truth**. Any analyst's publish (or a scheduled
  maintenance job) can rebuild them from the logs; they make auto-refresh and reporting fast
  without requiring every reader to fold potentially hundreds of log files from scratch. A
  `_meta/<table>.json` sidecar records which `ClientSeq` per analyst the snapshot already
  incorporates, so rebuilding is incremental (fold only newer rows), not a full replay.
- **Compaction:** log files grow indefinitely; a periodic maintenance pass (manual trigger or
  scheduled) can roll older entries (e.g. >90 days, configurable) out of the live `_changes/`
  files into dated archive files, since the table snapshot already reflects their net effect.
  Archives stay available for time-travel (§18a).
- **The meta-model syncs the same way.** Runtime schema changes (admin-created tables/columns,
  table metadata) are appended to per-writer logs under **`_meta/catalog/`** and folded
  deterministically — additive-only (never delete), first-wins structure, last-writer-wins table
  metadata. Every working copy applies the folded catalog **before** each data fold (and on
  creation), so a new table/column always lands before its data cells; cells for still-unknown
  columns are skipped and re-offered, never fatal. Table-level metadata (bilingual labels,
  navigation, the reference-picker display column) lives in a **`table_catalog`** meta table beside
  `column_catalog`. Runtime **operational settings** use a simpler shared document,
  **`_meta/settings.json`** (whole-file last-writer-wins — writes are rare and admin-only).

### Pluggable remote format (`IRemoteFormat`)
Both the per-writer logs and the materialized snapshots are written through the same format
abstraction, selected by the Config value `RemoteFormat`:

- **Parquet** (default) — typed, columnar, compact; types travel natively into Power BI/Excel.
  Lib: `Parquet.Net`.
- **CSV** — zero-runtime-dependency, universal, human-diffable, naturally append-friendly (a good
  fit for the per-writer logs specifically). Untyped, so Power Query applies types from the
  shipped catalog.
- **Excel (.xlsx)** — convenient for human inspection; **heaviest** and not recommended for logs
  or large snapshots. Lib: `ClosedXML` (MIT) — or the first-party OpenXML SDK if first-party-only
  is required.

**Switching format** on an existing store is a one-time rewrite of all logs + snapshots —
`build/convert-format.ps1`. Reporting templates are shipped per format (or parameterized).

**File access rule:** an analyst's own log file is the only canonical file they ever write to,
so the prior "never hold a live multi-machine write lock" rule is now automatically satisfied —
no other writer is ever touching it. Materialized-snapshot rebuilds still **atomically replace**
(temp file, then rename) since multiple analysts *could* race to rebuild the same snapshot; that
race is harmless (whoever writes last produces an equally valid fold) but atomicity avoids a
half-written file.

**Referential integrity across tables is eventually consistent**, by stable `Guid`. A mapping
referencing a not-yet-folded dictionary entry is flagged **unresolved** until that entry's log
entry is folded in; auto-refresh keeps the gap small.

**Schema (`column_catalog`) is itself one of the folded tables** — same mechanism, no special
case.

---

## 7. Sync, merge & rules

### 7a. Merge model (git-like, field-level, generic — revised for log-fold)
- **Publish = append, then fold.** The analyst's local change log entries since last publish are
  appended (as new rows) to their own `_changes/<analyst-id>.log` — a pure append, never a
  read-modify-write of someone else's data.
- A **3-way field-level check still runs locally before appending**, so the analyst still sees
  and resolves conflicts in the UI exactly as before: pull the latest fold (or incremental
  snapshot + newer log tails) → compute `(RowId, Column)` diffs against the analyst's `BaseVersion`
  → local-only → include; canonical-only → already there; both differ → **conflict**, resolved
  **keep mine / keep theirs / edit**. The conflict-resolution UX is unchanged; only *where the
  write lands* has changed (own log, not a shared file).
- After appending, the publishing analyst (or a background job) **refolds and rewrites the
  affected table snapshot(s)** for read performance — this is the one step that *is* a
  multi-writer race, mitigated by atomic replace (above); losing the race just means the next
  fold (auto-refresh or next publish) picks up the missed increment, since the logs themselves
  are never lost.
- An advisory per-table `*.lock` is no longer needed for the *write* (logs don't collide); a
  lightweight lock can still serialize *snapshot rebuilds* if avoiding redundant work matters,
  but correctness no longer depends on it.

### 7b. Rule AST ("exploitable format")
Serialized **JSON expression tree** + original text. Node types: `Literal`,
`FieldRef(DictionaryEntryId)`, `FunctionCall(name, args…)`, `RawText` (unparsed free text).

- **Predefined function library** defined as **metadata** `(Name, Signature, DescriptionEn/Fr)`
  → editor completion + validation, extensible without code changes.
- **Free-text allowed**: unresolved expressions stored as `RawText`, flagged, never block save.
- **Field references are linked, not literal** — accepting an autocomplete stores the
  DictionaryEntry **`Id`**, making lineage precise and rename-safe and enabling reference
  validation.
- **Editor efficiency**: inline dictionary autocomplete, function signature help, live reference
  validation, keyboard-driven entry — implemented per the approved mockup.

---

## 8. Audit trail (the change-log fold *is* the audit log)

The redesign in §6a means there's no separate publish-time "write to `logs`" step — **every
analyst's append-only log already is their slice of the audit trail**, and the canonical
`audit_log` view is the deterministic fold of all of them:

`(ChangeId, ChangeSetId, Table, RowId, Column, OldValue, NewValue, Operation, ChangedBy,
ChangedAtUtc, ClientSeq)`

- Every field edit is logged locally as it happens; on publish, the analyst's pending entries
  are appended verbatim to their own log — no transformation, so what's audited is exactly what
  was appended.
- Conflict resolutions are themselves logged (which side won, or the manual value), as an entry
  attributing the resolution to the resolving analyst.
- Imports write their own change-set entries, attributed to the importer/analyst running it.
- Reportable in Power BI as the fold of `_changes/*` (or the maintained `audit_log` snapshot, for
  speed — see §6a).

---

## 9. Auto-refresh

Background **`IHostedService`** timer (interval from Config), non-disruptive:
- Read the latest table snapshot + fold in any newer entries from each analyst's log past the
  snapshot's recorded `ClientSeq` (cheap — only the tail of each log, not a full re-fold).
- Fast-forward-merge only columns the analyst hasn't locally edited.
- Locally-edited columns that also changed remotely are **flagged for review at next publish**,
  not force-resolved.
- Never clobbers unpublished local edits.

---

## 10. Importer (Excel → DB)

Leverages the existing **ImportExcel** PowerShell module instead of adding an xlsx-read path to
the .NET app. Two distinct inputs feed it, which the pipeline keeps separate:

- **`TableDefinition.xlsx`** (Appendix A) — the **schema** definition (`Table | Field | Type |
  Formula`, one row per column across all entities). This seeds the **column catalog**, once,
  at provisioning time — it is not analyst data and is not re-imported on every run.
- **The team's live working workbook** (one worksheet per entity: Apps, Source, Dictionnary,
  Mapping, Config, …) — the **data** to import, validated against the catalog the first file
  produced. This is what the importer runs against repeatedly as analysts' current Excel is
  retired in favor of the app.

### 10a. Pipeline stages

```
TableDefinition.xlsx ──(once, at provisioning)──> seed-catalog.ps1 ──> column_catalog (local SQLite)

Working workbook ──> Import-Excel (per worksheet) ──> raw CSV/JSON per worksheet
                                                              │
                                                              ▼
                                              App.Importer (validate → transform → load)
                                                              │
                                                              ▼
                                          local SQLite (rows + import change-set in the
                                                         analyst's local change log)
```

1. **PowerShell** (`build/import.ps1`), using `ImportExcel`'s `Import-Excel`, reads each
   worksheet of the working workbook and exports it to a temp CSV/JSON, one file per worksheet.
   This isolates the .NET side from xlsx parsing entirely — `App.Importer` never opens an xlsx
   file directly.
2. **`App.Importer`** (a small console app, reusing `App.Domain`/`App.Application`/
   `App.Infrastructure.Local` — no separate stack) ingests each exported worksheet:
   - **Validate** every source column against the **column catalog** for the mapped table
     (type-check, required-field check, reference targets resolvable); rows that fail are
     **skipped, not aborted**, and written to an `import-errors.csv` report alongside a row
     number and reason, so a full run never blocks on a handful of bad rows.
   - **Transform**: apply the worksheet→table and column→column mapping (below), parse/normalize
     values per the catalog's data type (§2 of the Merge Engine doc's normalization rules apply
     here too, so imported values compare cleanly against future edits).
   - **Resolve identifiers**: the workbook's natural keys (e.g. `Apps.AppCode`,
     `Dictionnary.UniqueKey`) become lookups against already-imported rows to populate the real
     `Guid` foreign keys (`Source.IdApp`, `Dictionnary.Source ID`, …) — this is what turns the
     Excel's loose name-based lookups into proper reference columns (§4).
   - **Generate** a new `Guid Id` for any row not already matched by natural key (see re-run
     semantics below).
   - **Load** into the local SQLite working copy, and **write the whole run as one `import`
     change set** to the analyst's local change log — so an import is reviewable and revertible
     exactly like any other edit, and only becomes canonical once that analyst **publishes** (§7).
3. The importer runs **entity-by-entity in dependency order** (Apps → Source → Dictionnary →
   Config/Classification → Rule → Mapping), since later entities' FK-resolution step depends on
   earlier ones already being loaded.

### 10b. Worksheet → table mapping config

A per-worksheet mapping document drives steps 2–3. Mappings are **authored in the app's Data Import
wizard** (Mapping step) and **saved by name to the shared folder** (`_meta/import-mappings/<name>.json`)
so the team can adjust them as the working workbook's column headers drift, without a rebuild — and the
CLI importer runs the same saved mappings by name (it has no mapping files of its own). The document
shape:

```json
{
  "worksheets": [
    {
      "worksheet": "Apps",
      "table": "application",
      "naturalKey": ["AppCode"],
      "columns": {
        "ID": "id", "AppCode": "app_code", "Nom GDM": "name_gdm",
        "Nom VA360": "name_va360", "Nom Abrégé": "short_name",
        "Répertoire Valo (Gold)": "gold_root_path", "Description": "description",
        "Responsable TI": "responsible_it", "...": "..."
      }
    },
    {
      "worksheet": "Source",
      "table": "data_source",
      "naturalKey": ["Nom complet"],
      "columns": { "ID Source": "id", "IdApp": "application_id", "...": "..." },
      "references": { "application_id": { "table": "application", "by": "app_code", "viaColumn": "Code applicatif du système" } }
    }
  ]
}
```

- `naturalKey` — the column(s) used for **upsert matching** on re-run (§10c); proposed defaults
  are in §17 item 1, pending confirmation against real data.
  `columns` — straight header→catalog-column renames (this is also where the normalization pass'
  technical names, §4, get applied on the way in).
- `references` — for columns that need **FK resolution by natural key** rather than a literal
  copy (e.g. the workbook's `Code applicatif du système` should resolve to `Apps.AppCode` →
  `application_id`, not be stored as text).
- Unmapped worksheet columns are imported as-is into matching catalog columns if names align
  closely (warns in the report), or flagged **unmapped** rather than silently dropped.

### 10c. Re-runnable (upsert) semantics

The importer is designed to run **repeatedly** as the team's transition from Excel progresses
(e.g. re-run weekly while both systems are in use), not just once:

- Matching by `naturalKey` means a re-run **updates existing rows** (their `Guid` stays stable
  across runs) rather than duplicating them — critical since the workbook itself has no stable
  IDs today.
- A row present locally but **absent from the latest worksheet export** is left untouched (the
  importer never deletes); explicit removal stays a deliberate in-app action.
- Each run's change set is tagged `Operation = Import`, distinct from manual edits, so the audit
  trail (§8) can always show "this row arrived via the Apps import on 2026-06-20."

### 10d. Output & review

`App.Importer` prints (and writes to `import-report.json`): rows imported per worksheet, rows
updated vs newly created, rows skipped with reasons, and any **unresolved references** (a
`Code applicatif du système` that didn't match any `Apps.AppCode`, for instance) — surfaced the
same way the merge engine flags unresolved cross-table references (§6a), so the analyst running
the import sees exactly what needs manual cleanup before publishing.

The import lands in the **running analyst's local copy only**. Reviewing the imported rows in
the app and then **Publish**ing is a deliberate, separate step — so a bad import is caught and
discarded locally before it ever reaches the shared canonical store.

---

## 11. Reporting (read-only)

- Reporting consumes the **materialized remote outputs only**, read-only; the app (via its own
  publish/fold logic) is the sole writer, so reporting cannot corrupt canonical data and never
  touches the per-writer logs directly.
- Two materialized outputs, both rebuilt as part of the fold (§6a):
  - **Normalized table snapshots** — one file per table, FKs intact (lightweight, matches the
    operational model).
  - **Denormalized reporting views — opt-in, requested at publish time.** A Config-level (or
    per-table) flag `GenerateReportingView=true` tells the snapshot builder to additionally emit
    `<table>_report.parquet`: every reference/lookup column **resolved to its display value**
    and every **computed/calculated** column **evaluated**, flattened into the row — so a Power
    Query/Power BI author gets one table with everything, no joins needed, for tables they query
    often. Off by default per table to avoid rebuilding views nobody reads; analysts opt a table
    in once and every subsequent publish keeps it current.
- Templates in `reporting/`: a **Power Query** workbook and a **Power BI `.pbit`** that enumerate
  the synced folder and load every table — and every `*_report` view if present — so new
  tables/columns flow through automatically.
- **Per format:** Parquet → types native; CSV → types applied from the shipped catalog; Excel →
  read directly by Power Query.

---

## 12. Localization (EN / FR)

- User-facing strings in **`.resx`** → satellite assemblies; `IStringLocalizer`.
- Catalog carries **`LabelEn` / `LabelFr`** per column so dynamic editors are localized.
- Culture-aware date/number formatting; locale in Config. Carries over to the web version.

---

## 13. Tech stack & libraries

- **.NET 10 (LTS)**, **Blazor Hybrid** (`Microsoft.AspNetCore.Components.WebView.Wpf`, in-box
  WebView2 runtime on current Windows)
- `Microsoft.Extensions.Hosting` / `DependencyInjection` / `Configuration` / `Logging`
- **`Microsoft.Data.Sqlite`** (generic metadata-driven store; **EF Core not used**)
- `Microsoft.Extensions.Localization`
- `System.Text.Json` (in-box)
- **Format providers (all bundled):** `Parquet.Net` (Parquet) · CSV (in-box) ·
  `ClosedXML` *or* first-party OpenXML SDK (Excel)
- No Graph, no MSAL, no third-party rules engine, no third-party UI framework
- **ImportExcel** (PowerShell, existing) used only in the import pipeline, outside the app

---

## 14. Build & tooling (PowerShell)

- `dotnet publish` → **self-contained single-file** Windows exe (no runtime install, no admin).
- **provision** script — initialize a new canonical folder (`_changes/`, `_meta/`, catalog seed
  from `TableDefinition.xlsx`).
- **`build/import.ps1`** — ImportExcel → per-worksheet CSV/JSON → `App.Importer` (§10).
- **convert-format** script — rewrite all logs + snapshots between Parquet/CSV/Excel.
- **rebuild-snapshots** script — force a full re-fold of all per-writer logs into table snapshots
  (and reporting views where opted in); also the recovery tool if a snapshot is ever suspect.
- Optional maintenance scripts (compact/archive old log entries, inspect the audit fold).

---

## 15. Path to multi-tier web app — realized

Blazor Hybrid (§5a) made most of this free by construction; the web tier now **ships** as **App.Web**
(ASP.NET Core **Blazor Server**), and the path proved out as designed — "swap the host," not rewrite
the UI:

- **App.UI's Razor components are reused as-is** — the same metadata-driven editors, grid, and
  lineage graph render in the browser with no rewrite. Only **App.Desktop's thin host shell** is
  replaced by App.Web's. `IRemoteStore`, the column catalog, and `.resx` localization carry over
  unchanged.
- **Per-user workspaces (multi-user, multi-host).** Unlike the single-user desktop, one App.Web
  process serves many users: each authenticated user gets their **own isolated SQLite working copy +
  sync coordinator + import engine**, provisioned on first use, cached, and evicted when idle. The
  shared App.UI services (`ILocalStore`, `ICatalog`, `IAuditLog`, `ISyncCoordinator`,
  `ImportEngine`) become **scoped**, resolved per request/circuit from the current user's workspace
  via a scoped accessor — so the same components run unmodified against per-user state.
- **Affinity model B (host-namespaced writer ids).** A writer id is `<user>@<host>`, so the §6a
  per-writer-log invariant (only the owning writer writes their file) holds **per (user, host)**.
  Any host can serve any user, and **multiple App.Web hosts can run against the same shared folder
  concurrently** without their logs colliding — horizontal scale/availability with the §6a/§7a sync
  model intact. No server DB is required; the synced folder remains the system of record.
- **Identity & auth.** The acting user (§8 `changed_by`; the workspace key) is read from the
  `AuthenticationStateProvider` — the **circuit-safe** source, valid during prerender *and* on the
  live SignalR circuit (unlike `IHttpContextAccessor`, which is null on the circuit). Windows
  (Negotiate) auth is opt-in via `Auth:Require`; with auth off the host collapses to a single
  OS-account workspace for local dev / E2E.
- **Lifecycle safety.** A `CircuitHandler` tracks each user's live circuits so the idle sweeper never
  disposes a workspace an open session still holds (its scoped store is cached for the circuit's
  lifetime). A host-wide background service fast-forwards each active workspace from the shared fold
  (§9). The data directory is configurable (`DataDir`, default `App_Data`); per-user copies live
  under `<DataDir>/users/<user>`. A `/health` probe reports shared-folder reachability + active
  workspace count.

**Implementation note — sync-over-async on the circuit.** The format providers (§6/§7) expose a
synchronous `IRemoteFormat`; the Parquet provider bridges `Parquet.Net`'s async API. Called from the
Blazor Server circuit's single-threaded `SynchronizationContext`, a naive sync-over-async bridge
**deadlocks** (the library's continuations post back to the blocked thread). The bridge runs the async
core on the **thread pool** (no captured context) — the rule for any sync-over-async reached from a
circuit. (Desktop has no such context, so the desktop never hit this.)

For a purely server-side deployment, `IRemoteStore` could instead collapse into server-DB transactions;
the per-user/per-host model above keeps the offline-capable, synced-folder design as the shipping default.

### 15a. Security & user management

Authentication is **opt-in** (`Auth:Require`); with it off the host runs as a fully-privileged guest
admin (local dev / E2E). With it on, the **security module** (`App.Infrastructure.Identity`) adds:

- **Identity store (isolated, pluggable).** ASP.NET Core Identity over EF Core in a *separate* database —
  SQLite by default, SQL Server for multi-host — kept apart from the metadata-driven domain store and
  never synced to the shared folder. **This is the only EF Core in the solution.** Data Protection keys
  live in the same store, so hosts share a key ring (multi-host cookie/OIDC decryption).
- **Local login** via cookie auth. Sign-in is a static HTTP endpoint (a Blazor circuit can't set the auth
  cookie); the rest of the app stays interactive. Password policy + account lockout; a first-run
  bootstrap admin seeded from config.
- **RBAC.** Permissions (`Data.View/Edit/Publish/Import`, `Mappings.Manage`, `Lineage.View`,
  `History.View`, `Users.Manage`, `Roles.Manage`, `Security.Configure`, `App.Configure`) group into roles
  — seeded **Administrator / Publisher / Editor / Reader**, plus custom. A claims factory expands the
  user's roles into permission claims at sign-in; the server enforces one authorization **policy per
  permission**, and the shared components gate controls via `ICurrentUser.HasPermission` (defence in depth,
  not UI-only).
- **Admin UI** (`/admin/*`, permission-gated): user management (create, assign roles, enable/disable,
  unlock, reset password), a role/permission matrix, and a security audit viewer (logins, lockouts, admin
  actions).
- **Identity ↔ workspace.** `ICurrentUser` carries a stable `UserId` (the identity GUID) that keys the
  per-user workspace (§15) and attributes changes (§8), so a rename never forks a workspace.
- **Modern SSO (OIDC).** Zero or more OpenID Connect providers (Entra ID / Google / Okta / generic),
  config-driven (`Auth:Providers:Oidc`), surface as "Sign in with …" buttons. On the callback an
  external login is resolved to a local account — reuse an existing link, else link by verified email,
  else **just-in-time provision** with the provider's default role — and IdP **group/role claims map to
  app roles** (additively). The admin **Providers** view lists what's configured; secrets are supplied
  out-of-band.
- **Strong auth + self-service.** **Two-factor** (TOTP authenticator with a QR + one-time recovery codes;
  the login flow completes the second factor after the password step). **Self-service** flows —
  password reset, change password, opt-in registration, and email verification — as static HTTP endpoints
  (a circuit can't set the auth cookie) backed by Identity's token providers and an `IAppEmailSender`
  (SMTP, or a log fallback when unconfigured). Token generation is a testable service; the endpoints send
  the mail.
- **Hardening + admin depth.** A strict **Content-Security-Policy** (no inline scripts — the theme
  bootstrap is externalized — framing denied) + baseline headers on every response; **rate limiting** on
  the auth POST endpoints (per-IP fixed window) on top of per-account lockout; **session revocation**
  (an admin rotates a user's security stamp, invalidating their cookies within the stamp-validation
  interval — live circuits pick it up on reconnect); and an admin **dashboard** (user/role/lockout counts
  + recent security events).

- **Enforcement + lifecycle.** **Per-role MFA enforcement** (`Auth:Providers:Local:Mfa:Require` =
  `Administrators`/`All`): the claims factory marks non-enrolled covered users `mfa_pending` and the host
  confines them to the account pages until they enrol (the cookie is re-issued after enrolment). Admins
  can **invite by email** (a passwordless account + a set-password link, reusing the reset flow). The
  security schema ships as **EF migrations** — one migrations assembly per store provider (the supported
  pattern for a provider-pluggable context), applied at startup.
- **Desktop trust boundary.** The desktop shell stays deliberately **single-user**: it runs as the local
  OS account with a private working copy and full permissions — its trust boundary is the Windows session,
  and per-user authorization is the web host's job.

Passkeys (WebAuthn), SAML, Windows Negotiate as a provider, per-table ACLs, and a runtime provider-config
editor are **deliberately out of scope** — see `SECURITY.md` (threat model & posture) for the rationale.

---

## 16. Delivery phasing

- **Phase 1 — core**: metadata-driven generic store, **scalar columns only**, dynamic Blazor
  Hybrid editors, per-writer log store + snapshot fold (Parquet default, multi-format), field-level
  merge + conflict UI, audit trail (the fold itself), manual Publish, EN/FR, Excel importer.
- **Phase 2 — references & polish**: **reference-typed columns** + lineage participation,
  auto-refresh tuning, **opt-in denormalized reporting views**, format-conversion utility
  hardening, log compaction/archiving.
- Catalog, merge, and audit models are built reference-aware in Phase 1 so Phase 2 adds no
  schema migration.

---

## 17. Open items / pending inputs

1. **Natural keys for idempotent import** — proposed from the workbook: `application.app_code`,
   `data_source.name` (or `Nom complet`), `dictionary_entry.unique_key` (= `Source.Colonne`),
   `lookup_value.(lookup_type,value)`, `classification.prp`. Confirm these are unique in practice.
2. **Log retention/compaction threshold** — default proposed at 90 days live, archived after;
   confirm acceptable, and confirm archives only need to support time-travel (§18a), not routine
   reads.
3. **Excel-format library** — `ClosedXML` (ergonomic, MIT) vs OpenXML SDK (first-party, verbose);
   leaning ClosedXML unless first-party-only is required.
4. **Bilingual data** — `lookup_value` already carries FR/EN; confirm whether other free-text
   fields (Description, Notes, business names) also need paired FR/EN columns.
5. Still pending: **rule-editor UI mockups** (mockup built — confirm), and a **sample data extract**
   to validate import mapping and natural keys.

---

## 18. Upgrade candidates — resolved this round

1. **Per-writer append-only change logs (Q-U1) — ADOPTED.** Given ~10 concurrent analysts
   publishing 3–4×/day each (~30–40 publishes/day total against a handful of shared tables), the
   per-table snapshot model would see frequent OneDrive conflict copies. Each writer now appends
   to *their own* log file; writers never touch the same file, so same-table collisions are
   structurally eliminated rather than reconciled after the fact. Full redesign in §6/§7.
2. **Denormalized reporting views (Q-U2) — ADOPTED, opt-in at publish.** Rather than a parallel
   always-on star-schema snapshot, the analyst (or a Config flag) requests **per-table reporting
   views** be (re)materialized on publish — flat exports with every lookup resolved and every
   computed/calculated field evaluated, so Power Query/Power BI read one table with no joins.
   Detailed in §11.
3. **PII-aware exports (Q-U4) — WITHDRAWN.** The app is metadata-only (§1); it never holds or
   exports actual data, so masking/tokenization doesn't apply here. `classification` remains
   pass-through metadata for the downstream ETL engine to act on.
4. **UI fork (Q-U5) — RESOLVED: Blazor Hybrid now.** One UI for desktop + future web, prioritizing
   ease of the web transition over native desktop polish. Full rationale in §5a.

## 18a. Remaining upgrade candidates (under review)

1. **Validation / integrity engine** (Q-U3) — referential checks, required fields, natural-key
   uniqueness, and rule-reference resolution, surfaced at edit time and as a publish gate. Big
   value-add over the current Excel, which has none.
2. **SQLite FTS5 search** — first-party full-text index over expressions/descriptions to power the
   Excel-like global search quickly.
3. **Time-travel & version diff** — reconstruct any prior version and diff two versions from
   `audit_log` (lineage-over-time), enabled by the existing append-only design.

---

## Appendix A — Table & field definitions (from `TableDefinition.xlsx`)

Source workbook: single `mapping` sheet, columns `Table | Field | Type | Formula`. Types are the
analysts' SQL-style annotations; the app maps them to catalog data types. "Formula" captures lookup
(reference), autofill, and computed semantics verbatim.

### Apps  *(→ Application)*
| Field | Type | Notes |
|---|---|---|
| ID | UUID | PK |
| AppCode | VARCHAR(50) | |
| Nom GDM | VARCHAR(100) | |
| Nom VA360 | VARCHAR(100) | |
| Nom Abrégé | VARCHAR(100) | |
| Répertoire Valo (Gold) | VARCHAR(100) | |
| Description | VARCHAR(MAX) | |
| Responsable TI | VARCHAR(255) | |
| Responsable TI Délégué | VARCHAR(255) | |
| Responsable Affaire | VARCHAR(255) | |
| Responsable Affaire Délégué | VARCHAR(255) | |

### Source  *(→ DataSource)*
| Field | Type | Notes |
|---|---|---|
| ID Source | UUID | PK |
| Nom | VARCHAR(100) | |
| Description | VARCHAR(MAX) | |
| NB Fields | INT | **computed**: count of Dictionnary entries of source |
| Type | VARCHAR(100) | → Config (Type=SourceType) |
| Système | VARCHAR(100) | → Apps.Nom GDM, **autofill** on IdApp |
| Bloc | VARCHAR(100) | → Config (Type=BlocDefinition) |
| Fréquence | VARCHAR(100) | → Config (Type=Frequency) |
| Nom complet | VARCHAR(100) | |
| Extraction | VARCHAR(100) | |
| Caractéristiques | VARCHAR(100) | |
| Code applicatif du système | VARCHAR(100) | → Apps.AppCode, **autofill** on IdApp |
| Contrat d'échange | VARCHAR(255) | |
| Répertoires Ingestion | VARCHAR(255) | |
| IdApp | UUID | FK → Apps.ID |
| Répertoire Brute | VARCHAR(255) | bronze zone |
| Répertoire Standardisé | VARCHAR(255) | silver zone |
| Répertoire Valo (Gold) | VARCHAR(255) | gold zone |
| Date de Révision | TIMESTAMP | |
| Références révision | VARCHAR(255) | |
| Statut | VARCHAR(50) | → Config (Type=Status) |
| Notes | VARCHAR(MAX) | |
| Produit de données | VARCHAR(100) | → Config (Type=DataProduct) |

### Dictionnary  *(→ DataDictionary / DictionaryEntry)*
| Field | Type | Notes |
|---|---|---|
| ID | UniqueID | PK |
| UniqueKey | VARCHAR(255) | **computed**: `Source.Colonne`, autofill |
| Source ID | UUID | FK → Source.ID Source |
| Source | VARCHAR(100) | → Source.Source, autofill on Source ID |
| Colonne | VARCHAR(100) | |
| Ordre | INT | |
| Type de donnees | VARCHAR(25) | |
| Collation | VARCHAR(50) | |
| Cible - cle sk | Boolean | surrogate key |
| Cible - cle pk | Boolean | primary key |
| DIM Tables | VARCHAR(100) | → Source.Nom where Source.Type like '%DIM%' |
| Nullable | Boolean | |
| Nom Affaire | VARCHAR(100) | |
| Description | VARCHAR(MAX) | |
| Champ FSDF | VARCHAR(100) | |
| Langue | VARCHAR(25) | → Config (Type=Langue) |
| PRP - ID | VARCHAR(100) | → Classification.ClassificationPRP |
| Classification Accès 901 | VARCHAR(100) | **autofill** from Classification.Acces on PRP-ID |
| Classification Divulgation 902 | VARCHAR(100) | **autofill** from Classification.Divulgation on PRP-ID |
| Type de sécurité (tokenisation) | VARCHAR(100) | → Config (Type=Tokenisation) |
| Masque de données | VARCHAR(255) | |
| Exemples de données | VARCHAR(255) | |
| Date révision | TIMESTAMP | |
| Références révision | VARCHAR(255) | |
| Statut | VARCHAR(50) | → Config (Type=Status) |
| Bloc/cycle | VARCHAR(50) | → Config (Type=Bloc or Cycle) |
| Notes | VARCHAR(MAX) | |

### Mapping  *(kept in our expression model — see split below)*
**Core fields we keep:** `UniqueID` (PK), `ID dictionnaire` (target entry — store Id, show
UniqueKey), `ID champ ou regle`, `IDElement2`, `IDElement3`, `Cible - Règles de transformation`
(**the expression**), `Champs Tokenisé`, `Tokenisation Post Transformation`, `Champ FSDF`,
`Tables de paramètres`, `MISC`, `Date révision`, `Références révision`, `Statut` (→ Config Status),
`Notes`.

**Derived by the engine (not stored):** `Table`, `Cible table - Codes colonne`, `Table - DIM`,
`Tous les champs utilisés`, `Sources utilisées`, `Source - Système`, `ID Source`,
`Source - Fichier`, `Source - colonne`, `Source-Clé primaire`, `Source - Type de la colonne` —
all obtained by parsing the expression and following dictionary references (the lineage engine).

### Config
| Field | Type | Notes |
|---|---|---|
| Id | UUID | PK |
| Value | VARCHAR(255) | |
| DescriptionFR | VARCHAR(MAX) | |
| DescriptionEN | VARCHAR(MAX) | |
| Type | VARCHAR(100) | partition key (SourceType, Status, Frequency, BlocDefinition, DataProduct, Langue, Tokenisation, Bloc, Cycle, …) |

### Classification
| Field | Type | Notes |
|---|---|---|
| Id | UUID | PK |
| ClassificationPRP | VARCHAR(255) | referenced by Dictionnary.PRP-ID |
| ClassificationAcces | VARCHAR(255) | feeds Dictionnary Access-901 autofill |
| ClassificationDivulgation | VARCHAR(255) | feeds Dictionnary Disclosure-902 autofill |

> Note: the workbook has **no separate Rules sheet** — rules are referenced inside Mapping
> (`ID champ ou regle`). Our model promotes Rules to first-class reusable expressions (§7b) while
> staying compatible with that reference.
