<#
.SYNOPSIS
  Seeds the demo dataset (10 applications, 100 data sources, 5000 dictionary entries, plus a
  30-target / 5-level-deep Mapping Studio model) into a local working copy.

  By default it targets the desktop app's database (%LOCALAPPDATA%\MappingStudio\local.db) and RESETS
  it first (backing up the old one to *.bak) so you get a clean sample dataset. Close the desktop app
  before running. Use -DbPath to target another database, or -NoReset to seed only if it is empty.
.EXAMPLE
  ./build/seed-sample-data.ps1
  ./build/seed-sample-data.ps1 -DbPath "D:\path\to\local.db" -NoReset
#>
param(
    [string]$DbPath = (Join-Path $env:LOCALAPPDATA 'MappingStudio\local.db'),
    [switch]$NoReset
)
$ErrorActionPreference = 'Stop'

$dir = Split-Path -Parent $DbPath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

if (-not $NoReset -and (Test-Path $DbPath)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    Copy-Item $DbPath "$DbPath.$stamp.bak" -Force
    Write-Host "Backed up existing database to $DbPath.$stamp.bak"
    Remove-Item "$DbPath", "$DbPath-wal", "$DbPath-shm" -Force -ErrorAction SilentlyContinue
}

$project = Join-Path $PSScriptRoot '..\src\App.Importer\App.Importer.csproj'
dotnet run --project $project -c Release -- seed-sample $DbPath
