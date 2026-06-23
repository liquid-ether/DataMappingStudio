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
4. The app opens with the navigation tabs across the top (Applications, Sources, Dictionary, Config,
   Classification, Rules, Mappings, Lineage), an **EN / FR** language switch, and a **Publish** button.

### Where your data is kept
Your work is saved automatically on your PC under:

```
%LOCALAPPDATA%\MappingStudio
```

(That folder contains `local.db` — your working copy — and a `remote` folder.) You can paste that path
into File Explorer's address bar to find it.

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
3. Start it:

   ```powershell
   .\publish\web\App.Web.exe --urls "http://localhost:5000"
   ```

4. Open `http://localhost:5000` in a browser.

To use the shared folder, set the remote path before starting (env var or `appsettings.json`):

```powershell
$env:RemoteFolder = "C:\Users\<name>\OneDrive - Contoso\MappingStudio"
.\publish\web\App.Web.exe --urls "http://localhost:5000"
```

---

## 4. Importing existing Excel data (optional)

To load the team's current workbook into a local copy (one-time or repeatedly during the transition):

1. Install the free **ImportExcel** PowerShell module once:

   ```powershell
   Install-Module ImportExcel -Scope CurrentUser
   ```

2. Run the importer (it reads each worksheet, then validates/loads it as a reviewable change set):

   ```powershell
   ./build/import.ps1 -Workbook .\TheTeamsWorkbook.xlsx -DbPath "$env:LOCALAPPDATA\MappingStudio\local.db"
   ```

3. Open the desktop app, review the imported rows, then **Publish**. Which columns map to which fields
   is controlled by [`build/import-mapping.json`](build/import-mapping.json) — edit it as headers change.

---

## 5. Building from source (for developers)

### Requirements
- **.NET 10 SDK** (10.0.x) — download from <https://dotnet.microsoft.com/download/dotnet/10.0>.
- Windows (the desktop app is Windows-only; the libraries and web host are cross-platform).
- PowerShell 7+ for the `build/` scripts; **Pester** and **ImportExcel** modules only for their
  respective scripts/tests.

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
| `import.ps1` says ImportExcel is missing | Run `Install-Module ImportExcel -Scope CurrentUser`. |
| `dotnet` not recognized (building from source) | Install the .NET 10 SDK and reopen the terminal. |
