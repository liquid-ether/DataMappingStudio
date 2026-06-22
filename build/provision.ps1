<#
.SYNOPSIS
  Initializes a new canonical (synced) folder: _changes/ and _meta/ (Architecture §6a/§14).
.EXAMPLE
  ./build/provision.ps1 -RemoteFolder "C:\OneDrive\MappingStudio"
#>
param(
    [Parameter(Mandatory = $true)][string]$RemoteFolder
)
$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path (Join-Path $RemoteFolder '_changes') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $RemoteFolder '_meta') | Out-Null

Write-Host "Provisioned canonical folder: $RemoteFolder (_changes, _meta)."
