# Release Notes

All notable changes to Mapping Studio are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/), and the project uses
[Semantic Versioning](https://semver.org/). Each development phase ships as a minor pre-release
(`0.x.0`); the first feature-complete release will be `1.0.0`.

## [Unreleased]

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
