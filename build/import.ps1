<#
.SYNOPSIS
  Imports an Excel workbook into a local SQLite working copy (Architecture §10). The App.Importer CLI
  reads the .xlsx natively (ClosedXML), validates, transforms and resolves references, and loads it as
  one reviewable Import change set.
.DESCRIPTION
  -Mapping is the NAME of a mapping previously saved from the app's Data Import wizard (Mapping step) —
  the CLI only runs saved mappings; it has no mapping files of its own. Mappings are read from the
  shared folder's _meta/import-mappings (Importer:RemoteFolder in appsettings.json, or
  DMS_Importer__RemoteFolder). -DbPath is optional and defaults from appsettings.json
  (%LOCALAPPDATA%\MappingStudio\local.db, i.e. the app's own database).
  List what's available with: dotnet run --project src/App.Importer -- list-mappings
.EXAMPLE
  ./build/import.ps1 -Workbook .\working.xlsx -Mapping 'Team workbook'   # into the app's local database
.EXAMPLE
  ./build/import.ps1 -Workbook .\working.xlsx -Mapping 'Team workbook' -DbPath .\local.db
#>
param(
    [Parameter(Mandatory = $true)][string]$Workbook,
    [Parameter(Mandatory = $true)][string]$Mapping,
    [string]$DbPath,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

$importer = Join-Path $PSScriptRoot '..\src\App.Importer'
$cmd = @('import-excel', $Workbook, $Mapping)
if ($DbPath) { $cmd += $DbPath }

dotnet run --project $importer -c $Configuration -- @cmd
