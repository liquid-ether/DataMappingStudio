<#
.SYNOPSIS
  Forces a full re-fold of all per-writer logs into materialized table snapshots (Architecture §14).
  Also the recovery tool if a snapshot is ever suspect.
.EXAMPLE
  ./build/rebuild-snapshots.ps1 -DbPath .\local.db -RemoteFolder "C:\OneDrive\MappingStudio"
#>
param(
    [Parameter(Mandatory = $true)][string]$DbPath,
    [Parameter(Mandatory = $true)][string]$RemoteFolder,
    [string]$Format = 'parquet',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

$importer = Join-Path $PSScriptRoot '..\src\App.Importer'
dotnet run --project $importer -c $Configuration -- rebuild-snapshots $DbPath $RemoteFolder $Format
