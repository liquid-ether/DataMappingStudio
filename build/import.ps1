<#
.SYNOPSIS
  Imports an Excel workbook into a local SQLite working copy (Architecture §10). The App.Importer CLI
  now reads the .xlsx natively (ClosedXML) — no external ImportExcel module required — validates,
  transforms and resolves references, and loads it as one reviewable Import change set.
.DESCRIPTION
  -DbPath and -Mapping are optional: when omitted they default from the importer's appsettings.json
  (DbPath -> %LOCALAPPDATA%\MappingStudio\local.db, i.e. the app's own database; Mapping -> the bundled
  import-mapping.json). Override config without arguments via DMS_Importer__DbPath / DMS_Importer__MappingPath.
.EXAMPLE
  ./build/import.ps1 -Workbook .\working.xlsx                      # into the app's local database
.EXAMPLE
  ./build/import.ps1 -Workbook .\working.xlsx -DbPath .\local.db -Mapping .\build\import-mapping.json
#>
param(
    [Parameter(Mandatory = $true)][string]$Workbook,
    [string]$DbPath,
    [string]$Mapping,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

if ($Mapping -and -not $DbPath) {
    throw 'Specify -DbPath when passing -Mapping (the importer takes them positionally).'
}

$importer = Join-Path $PSScriptRoot '..\src\App.Importer'
$cmd = @('import-excel', $Workbook)
if ($DbPath) { $cmd += $DbPath }
if ($Mapping) { $cmd += $Mapping }

dotnet run --project $importer -c $Configuration -- @cmd
