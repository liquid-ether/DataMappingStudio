# Installing Mapping Studio

This guide covers four things:

1. **[Desktop app](#1-desktop-app-for-analysts)** — for analysts (the simplest option).
2. **[Shared folder](#2-shared-folder-for-it--administrators)** — so the team's edits sync (IT/admin).
3. **[Web version](#3-web-version-optional)** — open the app in a browser (optional).
4. **[Importing existing Excel data](#4-importing-existing-excel-data-optional)** and
   **[building from source](#5-building-from-source-for-developers)**.

If you only want to run the app on your own PC, do **section 1** and stop there.

---

## 1. Desktop app (for analysts)

The desktop app is `MappingStudio.exe` plus a tiny `wwwroot\index.html` file that must sit next to it
(keep them together in the same folder). Nothing else needs to be installed.

### Requirements
- **Windows 10 (version 1809 / build 17763) or newer, or Windows 11** (64-bit).
- Windows already includes the **WebView2 Runtime** the app uses. (On a rare older machine that lacks it,
  install it once — free — from Microsoft: search "Microsoft Edge WebView2 Runtime", download the
  **Evergreen Standalone Installer**, run it. No admin needed for the per-user installer.)
- **.NET runtime** — depends on which build you were given (your IT team picks one):
  - **Self-contained build (~70 MB)** — .NET is bundled in the file; you need **nothing else**.
  - **Compact build (~41 MB)** — smaller, but requires the **.NET 10 Desktop Runtime** to be installed
    once (free, from <https://dotnet.microsoft.com/download/dotnet/10.0> → *Run desktop apps* →
    "Windows x64"). If it isn't installed, Windows will offer to download it the first time you run the app.

### Steps
1. Get **`MappingStudio.exe`** and its **`wwwroot`** folder from your IT team (or build them — see
   section 5). Keep both together.
2. Put them anywhere convenient, e.g. your Desktop or `Documents` (the `wwwroot` folder must stay
   beside the exe).
3. **Double-click `MappingStudio.exe`.** The first launch takes a few seconds (it unpacks itself); after
   that it opens quickly.
4. The app opens with a **collapsible menu on the left** (Data model: Applications, Sources, Dictionary,
   Config, Classification, Rules · Studio: Mappings, Lineage · Tools: Import, History) and a top bar with
   the **EN / FR** language switch, a **colour-theme picker** (five palettes, each with light/dark), and
   the **Publish** button.

### Where your data is kept
Your work is saved automatically on your PC under:

```
%LOCALAPPDATA%\MappingStudio
```

(That folder contains `local.db` — your working copy — and a `remote` folder.) You can paste that path
into File Explorer's address bar to find it.

### Sample data (opt-in)
The app starts **empty** — a production install imports the real workbook (section 4). To explore with a
realistic demo dataset instead (10 applications, 100 data sources, 5000 dictionary entries, and a
30-target Mapping Studio model), either set the environment variable **`MAPPINGSTUDIO_SEED_SAMPLE=true`**
before the first launch (it seeds only an empty database), or run `build/seed-sample-data.ps1` at any
time (it backs up your current `local.db`, then reseeds).

> By itself the app works fully on your own machine. To **share and merge edits with colleagues**, point
> it at a shared folder — see section 2.

---

## 2. Shared folder (for IT / administrators)

Analysts share metadata through a **OneDrive-synced SharePoint folder** (a "git-like" sync). No server,
no database, no admin rights for the analysts.

### One-time setup
1. In SharePoint/OneDrive, create (or pick) a document-library folder for the team and make sure each
   analyst **syncs it to their PC** with OneDrive, so it appears as a normal local folder, e.g.
   `C:\Users\<name>\OneDrive - Contoso\MappingStudio`.
2. Initialize the folder once (on any one machine that has the tooling — see section 5 for PowerShell):

   ```powershell
   ./build/provision.ps1 -RemoteFolder "C:\Users\<name>\OneDrive - Contoso\MappingStudio"
   ```

   This creates the `_changes` and `_meta` subfolders the app uses.

### Point each analyst's app at the shared folder
On each analyst PC, set one environment variable to that synced folder, then restart the app:

```powershell
setx MAPPINGSTUDIO_REMOTE "C:\Users\<name>\OneDrive - Contoso\MappingStudio"
```

(Or: Start → "Edit environment variables for your account" → **New** → Name `MAPPINGSTUDIO_REMOTE`,
Value = the synced folder path.) After this, each analyst edits their own local copy and clicks
**Publish** to share; everyone's changes merge per-field, and conflicts are resolved in-app.

---

## 3. Web version (optional)

The same app can run in a browser (useful for shared/managed machines). It is published as a
self-contained folder — again, **no .NET install required** on the machine that runs it.

1. Build it (section 5): `./build/publish-web.ps1` → produces `publish\web\`.
2. Copy the `publish\web` folder to the host machine.
3. Start it (from any directory — the app anchors itself to the folder the exe lives in):

   ```powershell
   .\publish\web\App.Web.exe --urls "http://localhost:5000"
   ```

4. Open `http://localhost:5000` in a browser.

   The host's data (per-user working copies, the default `remote` folder, and — with authentication on —
   `security.db`) lives in an **`App_Data` folder beside the exe**, unless you point `DataDir`
   (`--DataDir "D:\mapping-data"` or the `DataDir` env var) somewhere else.

To use the shared folder, set the remote path before starting (env var or `appsettings.json`):

```powershell
$env:RemoteFolder = "C:\Users\<name>\OneDrive - Contoso\MappingStudio"
.\publish\web\App.Web.exe --urls "http://localhost:5000"
```

> **Production note — authentication.** The web host is **unauthenticated by default** (so local
> development and the E2E suite work without credentials), in which case all browsers share a single
> fully-privileged guest workspace. Before exposing it beyond localhost, turn on the **security module**
> by setting `Auth__Require=true` (or `Auth:Require` in `appsettings.json`). Users then **sign in** (local
> username/password), each gets their **own isolated working copy** keyed by their identity, and their
> **roles/permissions** gate every operation (view / edit / publish / import / admin). Without this, anyone
> who can reach the URL can read, edit, and publish.

```powershell
# First run: require sign-in and seed the initial administrator. Set the password out-of-band
# (user-secrets / env), NOT in appsettings.json. The security store (SQLite by default) is created
# automatically under the data dir; system roles (Administrator/Publisher/Editor/Reader) are seeded.
$env:Auth__Require = "true"
$env:Auth__BootstrapAdmin__Password = "<a strong password>"   # user "admin" unless changed
.\publish\web\App.Web.exe --urls "https://+:5001"
```

> **Security store & scale.** Identity/roles live in an **isolated** database, separate from the domain
> data and never synced to the shared folder — SQLite by default (`Auth:Store:Provider=Sqlite`), or a
> shared **SQL Server** (`Provider=SqlServer` + `Auth:Store:ConnectionString`) for multi-host. Multiple
> hosts must share **both** the security store and the Data Protection key ring (persisted in that store,
> keyed by `Auth:DataProtection:ApplicationName`) so a cookie issued by one host is accepted by another.
> Manage users, roles, providers and the audit trail from **/admin** once signed in as an administrator.

> **Self-service + two-factor.** Users can **reset** and **change** their password and enrol in **two-factor**
> authentication (TOTP authenticator + recovery codes) from the sign-in page / their account menu. Password
> reset, email verification and (opt-in) self-registration send email — configure SMTP under `Auth:Email`
> (`Host`, `Port`, `From`; password out-of-band via `Auth__Email__Password`). With no SMTP configured those
> links are logged (dev only) and admins can still create + reset users directly. Enable self-registration
> with `Auth:Providers:Local:AllowSelfRegistration=true`. To **require** two-factor, set
> `Auth:Providers:Local:Mfa:Require` to `Administrators` or `All` — covered users must enrol before using
> the app. With SMTP configured, admins can also **invite users by email** (Users → Invite by email): the
> invitee receives a set-password link. See **SECURITY.md** for the full threat model and posture.

> **Built-in hardening.** With auth on, the host applies a strict **Content-Security-Policy** + baseline
> security headers on every response, **rate-limits** the sign-in / reset endpoints per client IP (on top
> of per-account lockout), and lets an admin **revoke a user's sessions** (Users → Revoke sessions) — their
> cookies stop validating within a couple of minutes. The **/admin** dashboard summarises users, lockouts
> and recent security events.

> **Single sign-on (OpenID Connect).** To let users sign in with **Entra ID / Google / Okta / any OIDC**
> provider, add an entry under `Auth:Providers:Oidc` (an example is in `appsettings.json`). Set `Name`,
> `Authority`, `ClientId`, and `Enabled=true`; supply the secret out-of-band
> (`Auth__Providers__Oidc__0__ClientSecret`), and register the redirect URI `https://<host>/signin-oidc/<Name>`
> with the provider. First sign-in **auto-provisions** a local account (with `DefaultRole`) unless you turn
> that off; set `RoleClaimType` (e.g. `groups`) + `GroupRoleMap` to map IdP groups to app roles. Enabled
> providers appear as buttons on the sign-in page and in **/admin/providers**. Local login stays available
> alongside SSO.

> **Multi-user / multi-host.** One host serves many users (a working copy per user), and you can run
> **several hosts against the same shared folder** for scale or availability — writer ids are
> host-namespaced (`user@host`) so per-writer logs never collide. Each host stores its working copies
> under its data directory (`DataDir`, default `App_Data`; per-user copies under `<DataDir>/users/<user>`).
> Point every host at the same `RemoteFolder` to collaborate.

### Production deployment checklist

Work through this before exposing the web host to the team. The application code is release-ready; these
are the deployment knobs the rollout owner sets.

1. **Require authentication + seed the admin.** Set `Auth__Require=true` and, on first run, a strong
   `Auth__BootstrapAdmin__Password` (out-of-band, not appsettings). Each signed-in user then gets their
   **own isolated workspace** and their **roles/permissions** gate every operation; sign in as the admin and
   create users/roles under **/admin**. Choose the security store (SQLite for one host; SQL Server for
   multi-host) and, for multi-host, a shared Data Protection `ApplicationName`. Without auth, anyone who can
   reach the URL shares one fully-privileged workspace.
2. **Terminate TLS.** Serve over HTTPS — either bind Kestrel to an `https://` URL with a certificate, or
   (recommended) put the host behind a reverse proxy (IIS / Nginx) that terminates TLS and forwards. Behind
   a proxy, set `Proxy:Enabled=true` and list the proxy's IP(s) under `Proxy:TrustedProxies` — this makes
   rate limiting and the security audit see the **real client IP** (X-Forwarded-For) instead of the proxy,
   and redirects/auth see the original scheme. The startup log line `Failed to determine the https port for
   redirect` is expected when no HTTPS URL is bound directly.
3. **Point every host at the shared folder.** Set the same `RemoteFolder` (a OneDrive/SharePoint-synced
   path) on each host. Give **each host its own `DataDir`** (don't share working copies between hosts). The
   host probes the folder is writable at startup and warns clearly if not.
4. **Verify health + config at startup.** The host logs its effective configuration (data dir, shared
   folder, auth required) on boot. The anonymous **`/health`** endpoint reports shared-folder reachability
   and the active-workspace count — wire it into your load balancer / uptime monitor.
5. **Plan the shared folder.** It is the single point of coordination for all hosts and users: make sure it
   is backed up, has room to grow (per-writer logs + snapshots), and that the sync client (OneDrive) is
   healthy on every host. A locked/offline folder surfaces as a "shared folder unavailable" banner and a
   Degraded `/health`, not data loss — but publishing pauses until it recovers.
6. **Capacity sanity check.** Correctness is covered by the test suite (per-user isolation, cross-user
   publish, two hosts merging), but throughput under your real user/host count and folder latency has not
   been load-tested — run a brief multi-user smoke before going wide.
7. **Keep dependencies clean.** `build/check-dependencies.ps1` fails on any known-vulnerable NuGet package;
   run it (it's already a CI step) as part of each release build.
8. **Back up two things.** (a) The **shared folder** — it is the system of record (per-writer logs +
   snapshots); include it in the OneDrive/SharePoint retention or your backup schedule. (b) The
   **security store** (`security.db` under the data dir, or the SQL Server database) — it holds password
   hashes, roles and the Data Protection key ring; losing the key ring signs everyone out, losing the
   store loses accounts. Per-user working copies do **not** need backup (rebuildable from the shared
   folder; unpublished local edits are the only exposure).
9. **Observe it.** Publish/refresh durations, adopted cells and conflict counts are exposed as .NET
   metrics under the `MappingStudio` meter — watch live with `dotnet-counters monitor --counters
   MappingStudio -p <pid>`, or wire an OpenTelemetry exporter to that meter. `/health` covers liveness.
10. **Desktop distribution.** The published desktop `.exe` is unsigned by default — sign it
    (`signtool` with your org's code-signing cert) before wide distribution so SmartScreen doesn't warn,
    and plan updates via your software-deployment tooling (there is no built-in auto-update).

---

## 4. Importing existing Excel data (optional)

To load the team's current workbook into a local copy (one-time or repeatedly during the transition).
The importer reads the `.xlsx` **natively** — no extra PowerShell module needed.

1. Run the importer (it reads each worksheet, then validates/loads it as a reviewable change set).
   With no `-DbPath` it imports into the app's own database by default
   (`%LOCALAPPDATA%\MappingStudio\local.db`):

   ```powershell
   ./build/import.ps1 -Workbook .\TheTeamsWorkbook.xlsx
   ```

   Or call the CLI directly (same defaults, configurable via `src/App.Importer/appsettings.json` or
   `DMS_Importer__*` environment variables):

   ```powershell
   dotnet run --project src/App.Importer -- import-excel .\TheTeamsWorkbook.xlsx
   ```

2. Open the desktop app, review the imported rows, then **Publish**. Which columns map to which fields
   is controlled by [`build/import-mapping.json`](build/import-mapping.json) — edit it as headers change.

---

## 5. Building from source (for developers)

### Requirements
- **.NET 10 SDK** (10.0.x) — download from <https://dotnet.microsoft.com/download/dotnet/10.0>.
- Windows (the desktop app is Windows-only; the libraries and web host are cross-platform).
- PowerShell 7+ for the `build/` scripts; the **Pester** module only for the script tests. (The Excel
  importer reads `.xlsx` natively now — the `ImportExcel` module is no longer required.)

### Build, test, run
```powershell
dotnet build DataMappingStudio.slnx          # compile everything
dotnet test  DataMappingStudio.slnx          # run all tests (browser E2E skips unless DMS_E2E=1)
dotnet run --project src/App.Web             # run the web host locally
dotnet run --project src/App.Desktop         # run the desktop shell locally
```

### Produce the distributables
```powershell
./build/publish-desktop.ps1                      # -> publish\desktop\  (~70 MB exe, no .NET install needed)
./build/publish-desktop.ps1 -FrameworkDependent  # -> smaller (~41 MB exe), requires the .NET 10 Desktop Runtime
./build/publish-web.ps1                          # -> publish\web\  (self-contained web host)
```

Each desktop build produces `publish\desktop\MappingStudio.exe` **plus a `wwwroot\index.html`** host
page that must ship beside it — distribute the whole `publish\desktop` folder (or zip it). The exe is a
single `win-x64` file (the WebView2 loader, the SQLite native library and the WinRT projection are
bundled; the web assets are embedded in the bundled assemblies and resolved against the host page next
to the exe). The self-contained build additionally bundles the .NET runtime + WPF; the compact build
relies on the installed .NET 10 Desktop Runtime.

### Validate the desktop build
After publishing, run the startup smoke test — it launches the exe and confirms it gets past
WPF/WebView2 initialization without crashing:

```powershell
./build/smoke-desktop.ps1        # exit code 0 = pass (needs an interactive Windows desktop session)
```

If the app opens but a screen is blank with an error bar, check the per-launch log under
`%LOCALAPPDATA%\MappingStudio\logs` for the detailed exception.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| Double-clicking the exe does nothing / "WebView2 not found" | Install the **Microsoft Edge WebView2 Runtime** (Evergreen Standalone Installer), then retry. |
| "Could not load file or assembly 'Microsoft.Windows.SDK.NET'" | You have an old build. Rebuild with the current source (`./build/publish-desktop.ps1`) — the project now targets a versioned Windows TFM that includes this assembly. |
| Compact build: "To run this application, you must install .NET" | Install the **.NET 10 Desktop Runtime** (link above), or use the self-contained build. |
| SmartScreen warns about an unknown publisher | Click **More info → Run anyway** (the file isn't code-signed). Ask IT to sign it for wide distribution. |
| Edits aren't shared with colleagues | Confirm `MAPPINGSTUDIO_REMOTE` points at the **OneDrive-synced** folder and that OneDrive shows it as "up to date". Restart the app after changing the variable. |
| Web host: "address already in use" | Choose another port, e.g. `--urls "http://localhost:5050"`. |
| Importer can't find the mapping/workbook | Pass `-DbPath`/`-Mapping` explicitly, or set `DMS_Importer__DbPath` / `DMS_Importer__MappingPath`; the workbook path is relative to your current directory. |
| `dotnet` not recognized (building from source) | Install the .NET 10 SDK and reopen the terminal. |
