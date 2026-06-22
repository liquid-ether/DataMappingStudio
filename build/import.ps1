<#
.SYNOPSIS
  Imports the team's working workbook into a local SQLite working copy using the existing ImportExcel
  module (Architecture §10). Each worksheet is exported to JSON, then App.Importer validates/transforms/
  resolves and loads it as one reviewable Import change set.
.EXAMPLE
  ./build/import.ps1 -Workbook .\working.xlsx -DbPath .\local.db
#>
param(
    [Parameter(Mandatory = $true)][string]$Workbook,
    [Parameter(Mandatory = $true)][string]$DbPath,
    [string]$Mapping = (Join-Path $PSScriptRoot 'import-mapping.json'),
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    throw "The 'ImportExcel' module is required. Install it with: Install-Module ImportExcel -Scope CurrentUser"
}
Import-Module ImportExcel

$dataDir = Join-Path ([System.IO.Path]::GetTempPath()) ("dms-import-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

foreach ($sheet in (Get-ExcelSheetInfo -Path $Workbook)) {
    $rows = Import-Excel -Path $Workbook -WorksheetName $sheet.Name
    # -AsArray guarantees a JSON array even for a single row, which the importer expects.
    ($rows | ConvertTo-Json -Depth 6 -AsArray) | Set-Content -Path (Join-Path $dataDir ($sheet.Name + '.json')) -Encoding utf8
}

$importer = Join-Path $PSScriptRoot '..\src\App.Importer'
dotnet run --project $importer -c $Configuration -- import $DbPath $dataDir $Mapping
