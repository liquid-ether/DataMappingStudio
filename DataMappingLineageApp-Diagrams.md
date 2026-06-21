# Data Mapping & Lineage App — Diagrams

> Companion to the project reference. Three Mermaid schemas: the **normalized data model**,
> the **user workflow** once the app is built, and the **publish / conflict-resolution flow**.
> Names follow the normalized convention introduced in the architecture doc (§4 normalization pass):
> snake_case technical identifiers; French/business names live as catalog labels.
> Diagram 3 reflects the **per-writer append-only log** design (architecture §6a) — each analyst
> publishes by appending to their own file, never a shared one.

---

## 1. Data model (normalized ER)

Domain entities + the typed lookup and classification reference tables. Meta tables
(`column_catalog`, `audit_log`, `app_config`) are described below the diagram since they apply
generically to every table.

```mermaid
erDiagram
  application ||--o{ data_source : "has"
  data_source ||--o{ dictionary_entry : "defines"
  classification ||--o{ dictionary_entry : "classifies"
  dictionary_entry ||--o{ mapping : "is target of"
  rule ||--o{ mapping : "applied by"
  lookup_value ||--o{ data_source : "type / status / freq"
  lookup_value ||--o{ dictionary_entry : "language / status / security"
  lookup_value ||--o{ mapping : "status"
  mapping }o--o{ dictionary_entry : "uses (derived from expression)"

  application {
    uuid id PK
    string app_code
    string name_gdm
    string name_va360
    string short_name
    string gold_root_path
    string responsible_it
    string responsible_business
  }
  data_source {
    uuid id PK
    uuid application_id FK
    string name
    uuid type_lookup_id FK
    uuid status_lookup_id FK
    uuid frequency_lookup_id FK
    string bronze_path
    string silver_path
    string gold_path
    int field_count "computed"
  }
  dictionary_entry {
    uuid id PK
    uuid source_id FK
    string column_name
    int ordinal
    string data_type
    bool is_primary_key
    bool is_nullable
    string business_name
    uuid classification_id FK
    uuid language_lookup_id FK
    uuid status_lookup_id FK
    string unique_key "computed"
  }
  rule {
    uuid id PK
    string name
    json expression_ast
    string description
    uuid status_lookup_id FK
  }
  mapping {
    uuid id PK
    uuid target_entry_id FK
    uuid rule_id FK
    string kind "field|calc|join|filter"
    json expression_ast
    bool is_tokenized
    uuid status_lookup_id FK
    string notes
  }
  lookup_value {
    uuid id PK
    string lookup_type
    string value
    string description_fr
    string description_en
  }
  classification {
    uuid id PK
    string prp
    string access_901
    string disclosure_902
  }
```

**Meta tables (generic, not drawn above):**
- `column_catalog` — `(id, table_name, column_name, data_type{scalar|reference|computed}, label_en,
  label_fr, reference_target, formula, is_required, is_user_added, display_order)`. **Describes every
  table** and drives the dynamic UI editors. Folded the same way as any other table (§6a).
- `audit_log` — `(change_id, change_set_id, table_name, row_id, column_name, old_value, new_value,
  operation, changed_by, changed_at, client_seq)`. **Not a separate write target** — it *is* the
  deterministic fold of every analyst's own `_changes/<analyst-id>.log` (architecture §6a/§8); a
  materialized `audit_log` snapshot is kept for fast reads, same as any other table.
- `app_config` — `(key, value, scope{local|shared})`. Runtime settings (canonical path, remote format,
  refresh interval, locale). `local` rows never sync.

Every domain row also carries sync metadata `(row_version, modified_at, modified_by, is_deleted)`
and governance `(status_lookup_id, revised_at, revision_ref, notes)`. `row_version` is a local
edit counter (fast unchanged-row skip); conflict detection is per-cell against each analyst's
last-known fold, not a table-level version (architecture §6a/§7a).

---

## 2. User workflow (once built)

```mermaid
flowchart TD
  Launch["Launch app — Windows user context"] --> Load["Open local SQLite working copy"]
  Load --> Refresh["Background auto-refresh:<br/>pull latest from shared folder"]
  Refresh --> Browse["Browse / search catalog:<br/>Applications - Sources - Dictionary"]
  Browse --> Work{"Analyst task?"}

  Work -->|"Define"| Define["Register Application / Source<br/>add Dictionary entries"]
  Work -->|"Author"| Rules["Create reusable Rules<br/>functions + free expression"]
  Work -->|"Map"| Map["Excel-like grid:<br/>join / filter / field / calculated<br/>all as expressions"]
  Work -->|"Inspect"| Lineage["Lineage graph:<br/>recursive field derivation"]

  Define --> Save
  Rules --> Save
  Map --> Save
  Lineage --> Browse
  Save["Edits auto-saved locally<br/>appended to local change log"] --> More{"More work?"}
  More -->|"Yes"| Browse
  More -->|"No"| Publish["Publish"]
  Publish --> Merge["Append to own change log +<br/>conflict resolution (see diagram 3)"]
  Merge --> Shared[("Shared SharePoint folder<br/>per-writer logs + table snapshots")]
  Shared -. "read-only" .-> BI["Power BI / Excel reporting"]
  Refresh -. "keeps local fresh" .-> Browse
```

---

## 3. Publish / conflict-resolution flow

> Revised for the **per-writer append-only log** design (architecture §6a/§7a): an analyst's
> publish only ever *writes to their own log file*, so there is no shared file to lock or race —
> conflict detection still happens, but against a computed fold, not a contended file.

```mermaid
flowchart TD
  Start(["Analyst clicks Publish"]) --> Pull["Pull: read latest table snapshot<br/>+ fold newer tail of every<br/>analyst's _changes log past it"]
  Pull --> Diff["3-way field diff per RowId+Column<br/>BASE = analyst's last-known fold<br/>LOCAL = pending edits, REMOTE = fresh fold"]
  Diff --> Conf{"Conflicts?"}
  Conf -->|"No"| Build["Build the set of field changes<br/>to publish (local-only + resolved)"]
  Conf -->|"Yes"| Resolve["Resolution UI:<br/>keep mine / keep theirs / edit"]
  Resolve --> Build
  Build --> Append["APPEND new rows to this analyst's<br/>own _changes/&lt;analyst-id&gt;.log<br/>(no other writer ever touches this file)"]
  Append --> Snap["Rebuild affected table snapshot(s):<br/>refold logs newer than snapshot's ClientSeq<br/>per analyst, write via atomic temp+rename"]
  Snap --> Race{"Another analyst rebuilt<br/>the same snapshot meanwhile?"}
  Race -->|"Yes — harmless"| Note["Their fold already included this<br/>analyst's just-appended rows or will<br/>next cycle; no data lost either way"]
  Race -->|"No"| Done
  Note --> Done(["Local: BASE = fold just computed,<br/>clear pending edits — done"])
```

**Why there's no lock and no CAS retry here:** the only file an analyst ever writes
authoritatively is their **own** log — appends to *different* files never collide, so the
classic git-style "did the remote move while I was writing" race simply doesn't apply to the
canonical data. The **only** shared-write hazard left is the **snapshot rebuild** (a read-mostly
optimization), and that race is self-healing: whichever rebuild loses just means the next
auto-refresh or publish folds the missed increment in, because the underlying logs are never
overwritten or lost — only the derived snapshot lags briefly.

