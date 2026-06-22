<#
.SYNOPSIS
  Publishes the desktop app as a single Windows .exe.
  - Default: self-contained (no .NET install needed; ~70 MB) - best for non-technical users.
  - -FrameworkDependent: tiny (~a few MB) but requires the .NET 10 Desktop Runtime to be installed.
  Output: publish/desktop/MappingStudio.exe
.EXAMPLE
  ./build/publish-desktop.ps1
  ./build/publish-desktop.ps1 -FrameworkDependent
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\publish\desktop'),
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\src\App.Desktop\App.Desktop.csproj'
$selfContained = (-not $FrameworkDependent).ToString().ToLower()

$publishArgs = @(
    '-c', 'Release', '-r', $Runtime, '--self-contained', $selfContained,
    '-p:PublishSingleFile=true', '-p:DebugType=none', '-p:DebugSymbols=false', '-o', $Output
)
# Compression is only valid for self-contained single-file bundles.
if (-not $FrameworkDependent) { $publishArgs += '-p:EnableCompressionInSingleFile=true' }

dotnet publish $project @publishArgs

# Prune the non-runtime leftovers (XML doc files) so the distributable is a single .exe.
Get-ChildItem $Output -Recurse -File | Where-Object { $_.Extension -ne '.exe' } | Remove-Item -Force
Get-ChildItem $Output -Recurse -Directory | Sort-Object FullName -Descending | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

Write-Host ""
Write-Host ("Published {0} desktop app to: {1}" -f $(if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }), $Output)
Get-ChildItem $Output -Filter *.exe | ForEach-Object {
    Write-Host (" - {0} ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
}
