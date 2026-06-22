<#
.SYNOPSIS
  Rewrites all per-writer change logs between remote formats (Parquet / CSV / Excel), then prompts to
  rebuild snapshots (Architecture §6a/§14).
.EXAMPLE
  ./build/convert-format.ps1 -RemoteFolder "C:\OneDrive\MappingStudio" -From csv -To parquet
#>
param(
    [Parameter(Mandatory = $true)][string]$RemoteFolder,
    [Parameter(Mandatory = $true)][ValidateSet('parquet', 'csv', 'excel')][string]$From,
    [Parameter(Mandatory = $true)][ValidateSet('parquet', 'csv', 'excel')][string]$To,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

$importer = Join-Path $PSScriptRoot '..\src\App.Importer'
dotnet run --project $importer -c $Configuration -- convert-format $RemoteFolder $From $To
Write-Host "Now run rebuild-snapshots.ps1 -Format $To to refresh the materialized snapshots."
