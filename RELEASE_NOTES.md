# Release Notes

All notable changes to Mapping Studio are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/), and the project uses
[Semantic Versioning](https://semver.org/). Each development phase ships as a minor pre-release
(`0.x.0`); the first feature-complete release will be `1.0.0`.

## [Unreleased]

## [1.17.0] - 2026-07-01

### Fixed (critical) — publish state survives restarts
- **Persisted publish checkpoint.** The last-published `ClientSeq` was held only in memory, so every
  desktop relaunch / web workspace re-creation made **all historical edits look pending again** —
  inflating the Publish badge and resurrecting long-published edits as **spurious conflicts** when a
  colleague had since changed the same cell. The checkpoint now persists in the working copy
  (`sync_state` table via `IAuditLog.Get/SetPublishCheckpoint`) and is loaded on coordinator creation.
  Covered by a restart-simulation regression test.

### Added / Changed (high-priority hardening & scale)
- **Reverse-proxy awareness.** New `Proxy` config (`Enabled` + `TrustedProxies`): with it on, the host
  honours `X-Forwarded-For`/`-Proto` from the listed proxies, so **rate limiting buckets on the real
  client IP** (not one shared bucket for everyone behind the proxy) and the security audit records the
  actual client.
- **Bounded growth.** (a) Working copies of users not seen for `Workspaces:RetentionDays` (default 90)
  are now deleted by the background sweep — published work is safe in the shared folder, only long-idle
  local caches go; (b) security-audit events older than `Auth:Audit:RetentionDays` (default 365) are
  pruned daily by a new maintenance service; (c) **History** renders the newest 200 entries with a
  "Show more" pager instead of the entire audit trail (the DOM, not the data, was the bottleneck).
- **One fold per remote change, not per workspace.** A host-wide `RemoteFoldCache` (keyed by the remote
  version signature) is shared by all per-user workspaces, so after a publish the shared folder's logs
  are folded **once per host** instead of once per active workspace per refresh cycle. Verified by a
  counting-store test (two workspace creations → one fold).

## [1.16.0] - 2026-07-01

### Added — security phase 5 (final): migrations, MFA enforcement, invites, threat model
- **EF migrations for the security store.** The schema now ships as migrations — **one migrations assembly
  per provider** (`…Migrations.Sqlite` / `…Migrations.SqlServer`, the supported pattern for a
  provider-pluggable DbContext) — applied automatically at startup (`MigrateAsync`). Replaces
  `EnsureCreated`, so future schema changes upgrade existing deployments in place.
- **Per-role MFA enforcement.** `Auth:Providers:Local:Mfa:Require` = `None` (opt-in) | `Administrators` |
  `All`. Covered users who haven't enrolled are marked at sign-in and **confined to the account pages**
  until they set up their authenticator; the session is re-issued on enrolment.
- **Admin invite-by-email.** "Invite by email" in the Users admin creates a **passwordless** account with
  the chosen role and emails a set-password link (reuses the reset flow; shown only when SMTP is
  configured).
- **Threat model.** New `SECURITY.md`: assets, controls-by-threat, federation posture, the desktop
  trust-boundary decision (deliberately single-user), deploy-time requirements, and the explicit
  rationale for out-of-scope items (passkeys, SAML, per-table ACLs, runtime provider editor).

### Notes
- Tests: MFA policy marks only covered, non-enrolled users (and clears after enrolment); invite creates a
  passwordless user whose token sets the first password (Identity tests → 20). Full suite green.
- This closes the security roadmap's planned phases; remaining ideas are tracked as out-of-scope items in
  `SECURITY.md`.

## [1.15.0] - 2026-07-01

### Added — security phase 4: hardening + admin depth
- **Content-Security-Policy + security headers.** A strict CSP on every response (no inline scripts — the
  theme bootstrap moved to an external file — `frame-ancestors 'none'`, `object-src 'none'`), plus
  `X-Content-Type-Options`, `Referrer-Policy` and `X-Frame-Options`. The policy still allows what the app
  needs (inline styles, Google Fonts, `data:` QR images, same-origin SignalR).
- **Rate limiting.** The auth POST endpoints (login, password reset, register, 2FA) are throttled per
  client IP (fixed window) — brute-force protection on top of per-account lockout.
- **Session revocation.** Admins can **revoke a user's sessions** (Users → Revoke sessions); rotating the
  security stamp invalidates their existing cookies within the ~2-minute validation interval (live circuits
  pick it up on reconnect).
- **Admin dashboard** at **/admin**: user / disabled / locked / role counts, recent failed sign-ins, and a
  recent-security-events table; a Dashboard tab joins the admin sub-nav.

### Notes
- Tests: session revocation rotates the security stamp; the auth E2E asserts the CSP + `nosniff` headers
  and opens the dashboard. The E2E project now caps parallelism (`xunit.runner.json`) so its many
  host processes don't contend. Full suite green.
- Still designed for later phases: passkeys (WebAuthn), SAML, Windows Negotiate as a provider, per-table
  ACLs, admin invite-by-email, per-role MFA enforcement, and a runtime provider-config editor. The security
  store still uses `EnsureCreated` — EF migrations remain a pre-production follow-up.

## [1.14.0] - 2026-07-01

### Added — security phase 3: two-factor + self-service
- **Two-factor authentication (TOTP).** Users enrol an authenticator app from their account menu (QR + a
  manual key) and confirm a code; enabling 2FA issues **one-time recovery codes**. The login flow detects
  `RequiresTwoFactor` after the password step and completes on a **`/account/2fa`** page that accepts an
  authenticator code or a recovery code.
- **Self-service flows** (static HTTP endpoints — a Blazor circuit can't set the auth cookie):
  **password reset** (email link, account-enumeration-safe), **change password**, opt-in
  **self-registration** + **email verification**. Reachable from the sign-in page ("Forgot password?" /
  "Create account") and the account menu.
- **Email sender.** `IAppEmailSender` with an **SMTP** implementation (`Auth:Email`) and a **log fallback**
  when SMTP isn't configured (so admin-driven flows still work; self-service email needs SMTP in
  production). Token generation is separated from delivery, so the flows are unit-testable without SMTP.

### Notes
- Tests: `AccountService` (register + confirm, password reset, change password, **MFA enrol/enable/disable**
  verified with a real computed TOTP, email-sender selection). The auth E2E now also opens the
  change-password + 2FA-enrolment pages as the signed-in user. Full suite green.
- Deferred to later phases: passkeys (WebAuthn), SAML, Windows Negotiate as a provider, per-table ACLs,
  admin invite-by-email, per-role MFA enforcement, and a runtime provider-config editor.

## [1.13.0] - 2026-07-01

### Added — security phase 2: modern SSO (OpenID Connect)
- **OIDC providers (config-driven).** Zero or more OpenID Connect providers — **Entra ID / Google / Okta /
  generic** — configured under `Auth:Providers:Oidc` (name, authority, client id, scopes; secret supplied
  out-of-band). Enabled providers register as authentication schemes (authorization-code + PKCE, per-provider
  callback `/signin-oidc/<name>`) and surface as **"Sign in with …"** buttons on the login page. Local login
  remains available alongside SSO.
- **Account resolution on callback.** An external sign-in is resolved to a local account: reuse an existing
  link, else **link by verified email**, else **just-in-time provision** a new user (when the provider allows
  it) with its `DefaultRole`. Origin records the provider.
- **Group → role mapping.** `RoleClaimType` + `GroupRoleMap` map the IdP's group/role claims to app roles,
  granted **additively** at each sign-in (local role assignments are never stripped).
- **Admin Providers view** (`/admin/providers`, gated) lists configured providers + status, and a shared
  admin sub-nav (Users / Roles / Providers / Audit).

### Notes
- Tests: `ExternalSignInService` (JIT provisioning, email linking, group→role mapping, provisioning-off) and
  the provider catalog, over the real Identity store. Full suite + auth E2E green.
- Still designed for later phases: MFA (TOTP), self-service (invite / reset / email verify), passkeys, SAML,
  Windows Negotiate as a provider, per-table ACLs, and a runtime provider-config editor (providers are
  config-driven for now).

## [1.12.0] - 2026-07-01

### Added — security & user management (phase 1: local login + RBAC)
- **Opt-in authentication.** With `Auth:Require=true` the web host now requires **sign-in**; with it off it
  runs as a fully-privileged guest admin (local dev / E2E) exactly as before. Replaces the earlier
  Windows-Negotiate stub.
- **Isolated, pluggable security store.** A new `App.Infrastructure.Identity` project — **ASP.NET Core
  Identity over EF Core** in a database kept **separate** from the metadata-driven domain store and never
  synced to the shared folder. SQLite by default (`Auth:Store:Provider`), SQL Server for multi-host. This
  is the only EF Core in the solution. Data Protection keys persist in that store so multiple hosts share a
  key ring (multi-host cookie decryption).
- **Local login** (cookie auth via a static endpoint, since a Blazor circuit can't set the auth cookie),
  configurable **password policy** + **account lockout**, and a first-run **bootstrap admin** seeded from
  config.
- **Role-based access control.** Fine-grained permissions (`Data.View/Edit/Publish/Import`,
  `Mappings.Manage`, `Lineage.View`, `History.View`, `Users.Manage`, `Roles.Manage`, `Security.Configure`,
  `App.Configure`) grouped into roles — seeded **Administrator / Publisher / Editor / Reader**, plus custom
  roles. A claims factory expands roles into permission claims at sign-in; the server enforces **one policy
  per permission** and the shared components gate controls via `ICurrentUser.HasPermission` (Publish,
  Import, grid editing, the Admin area) — defence in depth, not UI-only.
- **Admin module** at `/admin` (permission-gated): **user management** (create, assign roles,
  enable/disable, unlock, reset password), a **role/permission matrix**, and a **security audit** viewer
  (logins, lockouts, admin actions).
- **Stable identity keys the workspace.** `ICurrentUser` now carries a stable `UserId` (the identity GUID),
  display name, roles and `HasPermission(...)`; the per-user workspace and change author key on `UserId`, so
  a rename never forks a workspace. Desktop stays single-user (fully privileged).

### Notes
- Modern SSO (OIDC — Entra/Google/Okta), MFA (TOTP), self-service (invite / reset / email verify), passkeys,
  SAML and per-table ACLs are designed and scheduled as later phases on this foundation.
- Tests: a new `App.Infrastructure.Identity.Tests` (seeding, permission expansion, user/role directory,
  audit), UI permission-gating (bUnit), and an auth-on browser E2E (redirect → sign in → app → sign out).

## [1.11.0] - 2026-06-30

### Added — per-user web workspaces (multi-user, multi-host)
- **Isolated working copy per user.** The Blazor Server host now serves **one workspace per authenticated
  user**: each user gets their own SQLite working copy, catalog, audit log, import engine and sync
  coordinator, provisioned on first use and cached (idle workspaces are evicted after 30 min). One host
  serves many users concurrently; the store/catalog/coordinator/import services resolve per-user from a
  scoped accessor. The previous single shared store is gone.
- **Affinity model B (host-namespaced writer ids).** A writer id is `user@host`, so **any host can serve
  any user** and multiple hosts can run against the same shared folder at once without their per-writer
  remote logs ever colliding. Published edits flow through the shared folder to that user's other hosts
  and to other users; unpublished edits stay private to the workspace.
- **Circuit-safe identity.** The current user is read from the `AuthenticationStateProvider` (which works
  both during prerender and on the live circuit, unlike `IHttpContextAccessor`). With `Auth:Require=true`
  the host requires an authenticated Windows user (Negotiate) and keys each workspace + change author by
  that identity; with auth off (dev / E2E) it collapses to a single OS-account workspace.
- **Configurable data dir.** The host data directory is now configurable via `DataDir` (command line,
  `appsettings`, or env), defaulting to `App_Data`; per-user copies live under `<DataDir>/users/<user>`.
  `/health` now reports shared-folder reachability plus the active-workspace count, and a host-wide
  background service fast-forwards every active workspace from the shared fold on an interval.

### Fixed
- **Web publish/refresh deadlock (critical).** The Parquet remote format bridges Parquet.Net's async API
  synchronously; called from the Blazor Server circuit's single-threaded `SynchronizationContext`, the
  library's continuations posted back to the blocked thread and **deadlocked every publish/refresh from
  the web**. The sync bridge now runs the async core on the thread pool (no captured context). A
  regression test drives the format under a single-threaded context to guard it.
- **Tolerate unknown remote tables.** `SyncCoordinator.Refresh` now skips remote change-log groups for
  tables not in the local catalog instead of throwing `no such table`, so a host stays healthy when the
  shared folder carries logs for tables it doesn't know (other versions / stale data).
- **Concurrent snapshot writes.** `SnapshotBuilder.Rebuild` is serialized so concurrent per-user publishes
  to the shared folder can't interleave a snapshot rebuild.
- **Never evict a live session's workspace.** A `CircuitHandler` tracks each user's open circuits, and the
  idle sweeper skips any user with a live circuit — so a workspace can't be disposed out from under an
  open browser tab (its scoped store is cached for the circuit's lifetime). Idle workspaces with no open
  circuit are still evicted after 30 min.

### Changed
- **Desktop demo seed is opt-in.** The desktop shell seeds the sample dataset only when
  `MAPPINGSTUDIO_SEED_SAMPLE=true` — a production install starts clean and imports the real workbook.
- **E2E hardening.** Each browser-test host runs in its own data dir (no cross-process SQLite contention);
  tests wait for the circuit to become interactive before acting and use accurate selectors. The suite
  now covers the per-user model (isolation, cross-user publish, two hosts merging through the shared
  folder) and a real web publish round-trip. The authenticated path is covered without a browser:
  `CircuitCurrentUser` resolves the signed-in Windows identity (and falls back to the OS account), the
  scoped accessor resolves an isolated workspace per user, and the idle sweeper preserves a live session.

## [1.10.0] - 2026-06-30

### Added / Changed (production hardening — medium-tier review items #7–#12)
- **#7 Supply-chain gate.** `build/check-dependencies.ps1` fails the build on any known-vulnerable NuGet
  package (including transitive); deprecated packages are reported as a warning. Wired as a CI step
  before build. Current scan: no vulnerable packages.
- **#8 Deeper import validation.** Column **max length** is now enforced — over-length values are
  truncated and flagged (the row still imports); malformed cells were already rejected and reported.
  `ImportReport` gained a `Warnings` list + `TotalWarnings`, and the wizard recap adds a Warnings metric
  and a "Rejections & warnings" panel listing the per-worksheet skip reasons + truncations.
- **#9 Config / transport hygiene.** The web host logs its **effective configuration** at startup (data
  dir, shared folder, auth required, seed) and **probes that the shared folder is writable**, warning
  clearly instead of failing silently during a later publish/sync.
- **#10 Accessibility.** ARIA across the metadata grid (sortable headers expose `aria-sort` + a spoken
  sort state, filter inputs get labels, the Filters toggle is `aria-pressed`, errors are `role="alert"`),
  the import wizard (stepper `aria-current`, dropzone label, progress bar `role="progressbar"`, live
  alerts), and the conflict dialog (`scope="col"` headers + grouped label); plus a dark-mode muted-text
  contrast bump.
- **#11 Test gaps.** Added the import max-length test and a Publish-page "shared folder unavailable"
  banner test (the conflict-resolution UI and the concurrency stress test were already covered).
- **#12 Observability.** An anonymous **`/health`** probe (`WorkingCopyHealthCheck`): Healthy when the
  local store is reachable and the shared folder is available, Degraded when the shared folder is down,
  Unhealthy when the store is unreadable. The Data Import wizard logs each outcome (user, file,
  created/updated/skipped) via `ILogger`.

Suite: **212 passing**.

## [1.9.0] - 2026-06-29

### Changed (production hardening — high-tier review items #5 & #6)
- **Import guard rails (#5).** The Data Import wizard now runs the import **off the UI thread** with a
  live **progress bar** and a **Cancel** button — rows already written are kept (re-running completes the
  rest, since the upsert is idempotent). Uploads are bounded: a file over **50 MB** or a workbook over
  **100,000 rows** is rejected with a friendly message before it can block the circuit. `ImportEngine.Run`
  gained optional `CancellationToken` and `IProgress<int>` parameters (it stops at the next row on cancel
  and returns the partial report; the CLI callers are unchanged).
- **Sync / OneDrive resilience (#6).** Remote file IO now **retries transient locks** (sync client /
  anti-virus) with backoff (`RemoteIo`); the sync coordinator records remote **health**
  (`ISyncCoordinator.RemoteStatus`) and the Publish page shows a banner when the shared folder is
  unavailable instead of the background refresh failing silently; and auto-refresh **skips the full
  refold when nothing changed remotely** — a cheap `IRemoteStore.RemoteVersion()` signature (file sizes
  + timestamps, no content read) — avoiding a needless 30-second scan of every table on each tick.

### Added (tests)
- Engine cancellation + per-row progress; oversize-file rejection; remote-unavailable health, skip-when-
  unchanged, and the version signature changing after an append. Suite: **210 passing**.

## [1.8.1] - 2026-06-29

### Fixed
- **The metadata editors fail gracefully.** Inline cell edits, add-row, delete and add-column now catch
  store failures and surface a friendly inline message instead of crashing the Blazor circuit
  (`MetadataGrid.TrySave`). The add-column flow validates the name up front — it must be a safe SQL
  identifier and not duplicate an existing column — and `SqliteCatalog.AddColumn` now rejects an unsafe
  identifier **before** inserting the catalog row, closing a gap where a bad name (e.g. one containing a
  space) persisted a phantom catalog column that then threw at `ALTER TABLE` and could break later
  schema/writes. New shared `SqlName.IsValidIdentifier` (App.Domain.Catalog) is the single source for
  the rule. Tests: catalog rejects the bad name without persisting; the grid shows validation/duplicate
  errors and survives a simulated store failure. Suite: **205 passing**.

## [1.8.0] - 2026-06-29

### Security & reliability (production hardening — top of the pre-production review list)
- **Concurrency-safe local store.** The process-wide SQLite connection is now serialized behind a
  re-entrant gate (`LocalDatabase.Gate`); every store/catalog/audit operation runs under it as one
  whole operation (preserving `INSERT`+`last_insert_rowid` and read-then-write atomicity). The web
  host's many Blazor circuits plus the 30-second auto-refresh timer can no longer execute commands on
  the shared connection concurrently (which risked "database is locked" / change-log corruption). New
  parallel read/write stress test asserts no corruption and unique change-log sequences.
- **Real change authorship.** New `ICurrentUser` (App.Application.Abstractions) records the acting user
  as `changed_by` on every change-log entry — the audit trail and conflict attribution previously
  recorded a hardcoded `"analyst"`. The desktop/CLI use the OS account; the web host uses the
  authenticated request user. Wired through `MetadataGrid`, `MappingStudioState`, `DataImportState`;
  the per-writer remote-log id (`SyncCoordinator.WriterId`, host-level) is set from the OS account.
- **Web authentication (opt-in).** `App.Web` can require an authenticated **Windows user (Negotiate)**
  for every endpoint via `Auth:Require=true` (or `Auth__Require=true`); that identity feeds
  `ICurrentUser`. Off by default so local dev and the E2E suite run without credentials. Verified:
  anonymous requests receive `401` when enabled, and the Data Import E2E still passes with it off.

## [1.7.0] - 2026-06-29

### Changed
- **The in-app Data Import wizard now performs a real import into the local database.** Replaced the
  mock (sample data + fake SQL-Server connection) with a live pipeline: an uploaded `.xlsx` is parsed by
  the shared workbook reader using the built-in mapping, previewed and **dry-run validated** against the
  column catalog, then loaded through the **same `ImportEngine` the CLI uses** into the local SQLite
  working copy as one reviewable Import change set (re-running upserts by natural key). The 6-step mock
  became **5 real steps**: Source (upload + worksheet preview) → Mapping (catalog-driven source→target)
  → Validate (rows that would import vs. skip, with reasons) → Load → Recap (the real `ImportReport`:
  created/updated/skipped + resolved references + ignored columns per worksheet). Reachable at `/import`
  and via the desktop **Import** tab.
- **Import engine moved to the Application layer** so the CLI and the UI share one engine:
  `App.Application.Importing` now owns `ImportEngine` (renamed from `Importer`), `ImportMapping`,
  `ImportReport`, `WorksheetData`, and the new `IWorkbookReader` abstraction. The ClosedXML reader is
  `ClosedXmlWorkbookReader` in `App.Infrastructure.Local` (behind `IWorkbookReader`, registered in DI);
  `App.Application.Provisioning.DefaultImportMapping` mirrors `build/import-mapping.json`. The CLI
  `import-excel`/`import` verbs are unchanged for callers.

### Added (tests)
- `DataImportWizardTests`: drive `DataImportState` end-to-end — a real `.xlsx` parsed and imported into
  the store (rows + FK resolution) plus dry-run validation of required-field skips. Suite: **200 passing**.

## [1.6.0] - 2026-06-29

### Added
- **Native Excel import in the importer (`import-excel`).** `App.Importer` now reads `.xlsx` workbooks
  **directly** (ClosedXML) and loads them into the local SQLite working copy as one reviewable Import
  change set — no external PowerShell `ImportExcel` module required. New verb:
  `import-excel <workbook.xlsx> [dbPath] [mapping.json] [report.json]`. New `ExcelWorksheetReader`
  takes the first used row of each mapped worksheet as headers and every subsequent row as a record
  (Excel's displayed cell values, so dates/numbers come through as shown). Absent/empty worksheets are
  skipped gracefully.
- **Importer configuration (`src/App.Importer/appsettings.json`).** A new `Importer` section
  (`DbPath`, `MappingPath`, `RemoteFolder`, `ChangedBy`), bound via `Microsoft.Extensions.Configuration`
  and overridable by `DMS_Importer__*` environment variables and explicit CLI arguments (arguments win).
  Defaults target the same working copy the desktop/web app uses
  (`%LOCALAPPDATA%\MappingStudio\local.db`) and the bundled `import-mapping.json`, so
  `import-excel <workbook.xlsx>` imports into the app's database with no further arguments.

### Changed
- `build/import.ps1` now drives the native `import-excel` path; the `ImportExcel` PowerShell module is
  no longer a prerequisite. The canonical `import-mapping.json` and `appsettings.json` ship next to the
  importer executable.

### Verified
- End-to-end against a local SQLite database: a generated `.xlsx` imported via the CLI created rows,
  a re-run reported them as updates (persistence via natural-key upsert), foreign keys resolved by
  natural key, and unmapped columns were reported; config/env-var-driven DB targeting confirmed with
  no `dbPath` argument.

### Added (tests)
- `ExcelImportTests`: a real in-memory `.xlsx` read by `ExcelWorksheetReader` and imported into a real
  `SqliteLocalStore` (rows + FK resolution + unmapped reporting), plus absent-worksheet handling.
  Suite: **197 passing**.

## [1.5.0] - 2026-06-29

### Added
- **Per-column sort and filter in every metadata editor.** Each column header in the entity editors
  (Applications, Sources, Dictionary, Config, Classification, Rules — all the same `MetadataGrid`) is
  now **click-to-sort**, cycling ascending → descending → unsorted with a ▲ / ▼ / ↕ indicator; and a
  **Filters** toggle in the toolbar reveals a filter box under each header. Sorting and filtering work
  on each column's *displayed* value, so reference columns sort/filter by their picker text and
  computed columns by their evaluated value, and numeric columns (`Integer` / `Number`) sort
  numerically rather than lexically. The toolbar shows a filtered/total count, a **Clear filters**
  action, and a "no rows match" hint; filters/sort drop automatically when a column is removed. EN/FR.

## [1.4.2] - 2026-06-29

### Changed
- **Extended the v1.4.1 "read each table once" fix to the remaining per-row reference/computed
  resolvers.** (The metadata grid fix in v1.4.1 already covers every catalog-driven table —
  Applications, Sources, Dictionary, Config, Classification, Rules — since they share one component.)
  - **Reporting views (`ReportingViewBuilder`).** Building a `<table>_report` resolved every reference
    with a per-row `GetById` and re-evaluated every computed column per row — so the `data_source`
    report re-scanned all 5000 `dictionary_entry` rows once per source (O(rows × table)). References
    are now resolved from one read of each referenced table and computed columns via the bulk
    `EvaluateColumn`, so a report over a large table reads each related table once.
  - **Mapping Studio grid (`MappingStudioState`).** The grid requested `KnownReferences` once per
    visible row, each call rebuilding the entire lineage scenario. The scenario and per-target
    known-reference lists are now cached and invalidated on any row add/edit/delete.

### Added
- Tests: a reporting-view test over multiple parents with different child counts (guards the bulk
  count map). Suite: **192 passing**.

## [1.4.1] - 2026-06-29

### Fixed
- **Severe scroll lag in the Sources and Dictionary editors with large datasets.** The metadata grid
  re-queried the database for *every* reference picker and recomputed *every* computed cell on each
  render — and QuickGrid re-renders visible rows on each scroll. Worst case, the Sources
  `field_count` column re-scanned **all 5000** `dictionary_entry` rows for **every visible row, every
  scroll frame**, so the grid went blank and took 10–15 s to repaint on the sample dataset. Reference
  options and computed values are now derived **once per load** — a new bulk
  `IComputedEvaluator.EvaluateColumn` reads each child/target table a single time and maps every row
  from memory — and the cached values are reused across renders; an edited row refreshes just its own
  computed cells in place. (`ReferenceService`, `MetadataGrid`.)
- **Slow desktop first paint on machines with no/slow/firewalled internet.** The Google Fonts
  stylesheet was render-blocking, so the WebView could stall for seconds (up to a TCP timeout) waiting
  on the network before showing anything. The font stylesheet now loads asynchronously in both hosts;
  first paint uses the Inter / JetBrains Mono fallbacks immediately and swaps the real fonts in when
  they arrive. (The one-time sample-data seed was measured at ~1 s for 5388 rows and is not the cause.)

### Added
- Tests: `EvaluateColumn` count + lookup coverage and a grid assertion for the precomputed
  `field_count` value. Suite: **191 passing**.

## [1.4.0] - 2026-06-29

### Added
- **Data Import tool** — a new six-step import wizard (*Source → Mapping → Transform → Validate →
  Load → Recap*), ported from the approved "Data Import Tool" UI design. It covers drag-and-drop
  file intake with parsing options and a live encoding-aware preview, name-similarity column mapping
  to a target table (with confidence, type-mismatch and key/required flags), per-column transform
  chains with a live before/after preview, a dry-run validation summary with a rejection log and load
  modes (append / truncate+insert / upsert / SCD), an animated load run, and an audit recap with
  filterable status metrics and charts. Backed by `DataImportState` (App.UI.DataImport); reachable at
  `/import` on the web host and via the **Import** tab on the desktop shell. EN/FR throughout.
- **App-wide light/dark theme.** A new `ThemeState` (App.UI.Theme) toggles a `data-theme` attribute on
  the document root and persists the choice in `localStorage`; a host bootstrap script applies the
  saved (or system-preferred) theme before first paint to avoid a flash. A theme toggle now sits in
  the top bar of both hosts.

### Changed
- **The global design system is re-aligned to the Data Import tool's look.** `app.css` now defines a
  single set of design tokens at `:root` (light) with a `:root[data-theme="dark"]` override, switches
  the type to **Inter** + **JetBrains Mono**, and adopts the teal accent and surface palette. The
  original token names are aliased onto the new palette so every existing component (top bar, grids,
  editors, lineage) themes automatically in both light and dark. The dark top bar is now a light
  surface bar matching the importer chrome.
- The Data Import wizard no longer carries its own scoped palette/theme toggle — it consumes the
  global theme like the rest of the app.

## [1.3.1] - 2026-06-23

### Changed
- **Tables now auto-size to the window.** The app shell is a full-height flex column (top bar +
  fill-the-rest content area), so each grid expands to take most of the available space and
  re-flows on resize, instead of being capped at a fixed width/height. The grid scrolls internally
  with a sticky header. The buffers kept around the content are **configurable** via CSS variables
  in `app.css` (`--buffer-x`, `--buffer-top`, `--buffer-bottom`, and `--content-max` to optionally
  cap width). This also gives QuickGrid a proper sized scroll container for virtualization.

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
