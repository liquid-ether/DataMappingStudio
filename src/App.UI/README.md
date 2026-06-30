# App.UI — shared Blazor component library

A metadata-driven Razor Class Library (RCL) that provides Mapping Studio's entire UI. The **same
components render unchanged** in both hosts:

- **App.Desktop** — Blazor Hybrid (WPF) shell, single user, local working copy.
- **App.Web** — ASP.NET Core Blazor Server, one isolated workspace per user (see the architecture
  reference, §15).

Because the UI is driven by the column **catalog** rather than hand-written per-entity forms, the same
grid/editor renders every entity, and new tables/columns appear with no new components.

## Components

| Component | Purpose |
|---|---|
| `MsTopBar` | App chrome: brand, catalog-driven tabs, instant EN/FR toggle, light/dark toggle, Publish. |
| `MetadataGrid` | The cornerstone — renders rows/columns from the catalog with inline edit, per-column sort/filter, add-row, add-column, required validation, virtualized scrolling. |
| `MappingGrid` | Mapping Studio: target/source mappings grouped by target, with the expression editor. |
| `ExpressionEditor` / `ExpressionView` | Rule-expression authoring (autocomplete, classification) and read-only highlighted render. |
| `LineageView` | Field-level lineage graph (SVG) + upstream "built from" side panel. |
| `DataImportWizard` | Excel import: Upload → Source → Mapping → Validate → Load, with a recap. |
| `PublishPanel` | Publish pending changes; preview the 3-way merge; resolve conflicts. |
| `ConflictDialog` | Field-level conflict resolution UI. |
| `HistoryView` | The audit trail (the change-log fold). |
| `DesktopRoot` | Non-routed view switcher for the Hybrid shell (the web host routes to the same components via its own pages). |

## Using it from a host

1. **Reference** the project (or drop the assembly in) and register the UI services. The single
   entry point wires localization, the UI state services (`LanguageState`, `ThemeState`,
   `MappingStudioState`, `DataImportState`) and the reference/mapping services:

   ```csharp
   services
       .AddApplication()      // rule/lineage engines, sync coordinator, import engine
       .AddLocalStore()       // ILocalStore / ICatalog / IAuditLog (SQLite)
       .AddRemoteStore(...)   // IRemoteStore + snapshots (synced folder)
       .AddAppUi();           // <-- this library
   ```

   The components inject abstractions owned by `App.Application` (`ILocalStore`, `ICatalog`,
   `ISyncCoordinator`, `ILineageEngine`, `ICurrentUser`, …); a host must register an implementation
   of each (the in-repo `App.Infrastructure.Local` / `App.Infrastructure.Remote` provide them). The
   web host resolves the data-bound services **per user** — see `App.Web/Workspaces`.

2. **Link the stylesheet.** The design system ships as a static web asset; reference it from the
   host's root document:

   ```html
   <link rel="stylesheet" href="_content/App.UI/css/app.css" />
   ```

   All design tokens (light/dark palette, type, spacing) live in that file; `ThemeState` toggles the
   `data-theme` attribute on `<html>`.

3. **Render** the components — the desktop shell renders `DesktopRoot`; the web host routes its pages
   to the same components.

## Localization

EN/FR via `.resx` under `Resources/` plus an instant-toggle `LanguageState`, so switching language
re-renders without a reload. `AddAppUi()` registers both paths.
